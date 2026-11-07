using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderInventory.Application.DTOs.Auth;
using OrderInventory.Domain.Entities;
using OrderInventory.Infrastructure.Persistence;
using Xunit;

namespace OrderInventory.IntegrationTests;

public sealed class OrderConcurrencyTests
{
    [Fact]
    public async Task TwoParallelOrdersForLastUnit_ReturnOneCreatedAndOneConflict()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ORDERINVENTORY_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "Set ORDERINVENTORY_TEST_CONNECTION to a dedicated SQL Server test database. " +
            "The test uses real SQL Server rowversion and transaction behavior.");

        await using var factory = new OrderInventoryApiFactory(connectionString!);
        using var client = factory.CreateClient();

        // Starting the client runs migrations and the development seed routine.
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "customer1@example.com",
            Password = "ChangeMe123!"
        });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        int productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sku = $"CONCURRENCY-{Guid.NewGuid():N}";
            var product = new Product
            {
                Sku = sku,
                Name = "Last unit concurrency test",
                Price = 100m,
                StockQuantity = 1,
                IsActive = true
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        async Task<HttpResponseMessage> SendOrder(string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new
                {
                    Items = new[] { new { ProductId = productId, Quantity = 1 } }
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        var responses = await Task.WhenAll(
            SendOrder($"parallel-a-{Guid.NewGuid():N}"),
            SendOrder($"parallel-b-{Guid.NewGuid():N}"));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remainingStock = await verifyDb.Products
            .Where(x => x.Id == productId)
            .Select(x => x.StockQuantity)
            .SingleAsync();
        Assert.Equal(0, remainingStock);
    }

    private sealed class OrderInventoryApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["JwtSettings:SecretKey"] = Convert.ToBase64String(
                        Enumerable.Range(0, 64).Select(x => (byte)x).ToArray()),
                    ["JwtSettings:Issuer"] = "OrderInventory.Tests",
                    ["JwtSettings:Audience"] = "OrderInventory.Tests"
                });
            });
        }
    }
}

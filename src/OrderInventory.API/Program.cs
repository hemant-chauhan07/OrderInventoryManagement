using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderInventory.API.Extensions;
using OrderInventory.API.BackgroundServices;
using OrderInventory.API.Middleware;
using OrderInventory.Application.Interfaces;
using OrderInventory.Domain.Entities;
using OrderInventory.Infrastructure.Authentication;
using OrderInventory.Infrastructure.Persistence;
using OrderInventory.Infrastructure.Services;


var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// 1. Controllers
// --------------------------------------------------

builder.Services.AddControllers();

builder.Services.AddProblemDetails();

// --------------------------------------------------
// 2. SQL Server
// --------------------------------------------------

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString(
            "DefaultConnection"));

    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

// --------------------------------------------------
// 3. Password hashing
// --------------------------------------------------

builder.Services.AddScoped<IPasswordHasher<User>,
    PasswordHasher<User>>();

// --------------------------------------------------
// 4. Authentication services
// --------------------------------------------------

builder.Services.AddScoped<IAuthenticationService,
    AuthenticationService>();

builder.Services.AddScoped<IProductService, ProductService>();

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddHostedService<PendingOrderCancellationService>();

// --------------------------------------------------
// 5. JWT Authentication
// --------------------------------------------------

var jwtKey = builder.Configuration[
    "JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException(
        "JWT secret key is missing.");

var keyBytes = Convert.FromBase64String(jwtKey);

if (keyBytes.Length < 64)
{
    throw new InvalidOperationException(
        "JWT secret key must contain at least 64 bytes.");
}

var signingKey = new SymmetricSecurityKey(keyBytes);

var issuer = builder.Configuration[
    "JwtSettings:Issuer"];

var audience = builder.Configuration[
    "JwtSettings:Audience"];

if (string.IsNullOrWhiteSpace(issuer) ||
    string.IsNullOrWhiteSpace(audience))
{
    throw new InvalidOperationException(
        "JWT issuer and audience must be configured.");
}

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,

                ValidateLifetime = true,

                ClockSkew = TimeSpan.FromSeconds(30),

                NameClaimType =
                    System.Security.Claims.ClaimTypes.Name,

                RoleClaimType =
                    System.Security.Claims.ClaimTypes.Role
            };
    });

builder.Services.AddAuthorization();

// --------------------------------------------------
// 6. Login Rate Limiting
// --------------------------------------------------

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext =>
    {
        var ipAddress =
            httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,

                Window = TimeSpan.FromMinutes(1),

                QueueLimit = 0,

                AutoReplenishment = true
            });
    });
});

// --------------------------------------------------
// 7. Swagger
// --------------------------------------------------

builder.Services.AddSwaggerWithJwt();

// --------------------------------------------------
// 8. Memory Cache
// --------------------------------------------------

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IProductCacheInvalidator, ProductCacheInvalidator>();

// --------------------------------------------------
// 9. Build application
// --------------------------------------------------

var app = builder.Build();

// --------------------------------------------------
// 10. Seed database
// --------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    await DatabaseSeeder.SeedAsync(context);
}

// --------------------------------------------------
// 11. Middleware pipeline
// --------------------------------------------------

app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

// --------------------------------------------------
// 12. Swagger
// --------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --------------------------------------------------
// 13. Map controllers
// --------------------------------------------------

app.MapControllers();

app.Run();

// Required for WebApplicationFactory integration tests.
public partial class Program
{
}
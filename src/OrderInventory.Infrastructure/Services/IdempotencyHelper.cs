using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderInventory.Application.DTOs.Orders;

namespace OrderInventory.Infrastructure.Services
{
    public static class IdempotencyHelper
    {
        public static string CalculateRequestHash(
            CreateOrderRequestDto request)
        {
            var normalizedItems = request.Items
                .GroupBy(x => x.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.ProductId)
                .ToList();

            var json = JsonSerializer.Serialize(normalizedItems);

            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(json));

            return Convert.ToHexString(bytes);
        }
    }
}

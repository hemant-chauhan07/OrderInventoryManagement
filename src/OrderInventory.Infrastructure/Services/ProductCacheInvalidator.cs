using Microsoft.Extensions.Caching.Memory;
using OrderInventory.Application.Interfaces;

namespace OrderInventory.Infrastructure.Services
{
    public class ProductCacheInvalidator : IProductCacheInvalidator
    {
        private readonly IMemoryCache _cache;

        public ProductCacheInvalidator(IMemoryCache cache)
        {
            _cache = cache;
        }

        public void Invalidate()
        {
            _cache.Set(
                "products:cache-version",
                Guid.NewGuid().ToString("N"),
                TimeSpan.FromDays(1));
        }
    }
}

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.DTOs.Products;
using OrderInventory.Application.Exceptions;
using OrderInventory.Application.Interfaces;
using OrderInventory.Domain.Entities;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.Infrastructure.Services
{
    public class ProductService : IProductService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ProductService> _logger;

        private static readonly TimeSpan CacheDuration =
            TimeSpan.FromMinutes(5);

        public ProductService(
            ApplicationDbContext context,
            IMemoryCache cache,
            ILogger<ProductService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        // --------------------------------------------------
        // GET PRODUCTS WITH FILTERING, SORTING AND PAGINATION
        // --------------------------------------------------

        public async Task<PagedResponseDto<ProductResponseDto>>
            GetProductsAsync(ProductQueryDto query)
        {
            ValidateQuery(query);

            var cacheKey = BuildCacheKey(query);

            if (_cache.TryGetValue(
                cacheKey,
                out PagedResponseDto<ProductResponseDto>? cached))
            {
                _logger.LogInformation(
                    "Product cache hit: {CacheKey}",
                    cacheKey);

                return cached!;
            }

            _logger.LogInformation(
                "Product cache miss: {CacheKey}",
                cacheKey);

            IQueryable<Product> products = _context.Products
                .AsNoTracking()
                .Where(x => x.IsActive);

            // Search by product name
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();

                products = products.Where(x =>
                    EF.Functions.Like(x.Name, $"%{search}%"));
            }

            // Minimum price
            if (query.MinPrice.HasValue)
            {
                products = products.Where(x =>
                    x.Price >= query.MinPrice.Value);
            }

            // Maximum price
            if (query.MaxPrice.HasValue)
            {
                products = products.Where(x =>
                    x.Price <= query.MaxPrice.Value);
            }

            // In-stock filter
            if (query.InStock.HasValue)
            {
                products = query.InStock.Value
                    ? products.Where(x => x.StockQuantity > 0)
                    : products.Where(x => x.StockQuantity == 0);
            }

            // Get total count before pagination
            var totalCount = await products.CountAsync();

            // Sorting
            products = ApplySorting(
                products,
                query.SortBy,
                query.SortDirection);

            // Pagination happens in SQL Server
            var items = await products
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(x => new ProductResponseDto
                {
                    Id = x.Id,
                    Sku = x.Sku,
                    Name = x.Name,
                    Price = x.Price,
                    StockQuantity = x.StockQuantity,
                    IsActive = x.IsActive,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                })
                .ToListAsync();

            var response = new PagedResponseDto<ProductResponseDto>
            {
                Items = items,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(
                    (double)totalCount / query.PageSize)
            };

            _cache.Set(
                cacheKey,
                response,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                });

            return response;
        }

        // --------------------------------------------------
        // GET PRODUCT BY ID
        // --------------------------------------------------

        public async Task<ProductResponseDto> GetProductByIdAsync(int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ProductResponseDto
                {
                    Id = x.Id,
                    Sku = x.Sku,
                    Name = x.Name,
                    Price = x.Price,
                    StockQuantity = x.StockQuantity,
                    IsActive = x.IsActive,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                })
                .FirstOrDefaultAsync();

            if (product == null)
            {
                throw new ApiException(
                    404,
                    "Product not found.");
            }

            return product;
        }

        // --------------------------------------------------
        // CREATE PRODUCT
        // --------------------------------------------------

        public async Task<ProductResponseDto> CreateProductAsync(
            CreateProductRequestDto request)
        {
            var sku = request.Sku.Trim().ToUpperInvariant();

            var exists = await _context.Products
                .AnyAsync(x => x.Sku == sku);

            if (exists)
            {
                throw new ApiException(
                    409,
                    "A product with this SKU already exists.");
            }

            var product = new Product
            {
                Sku = sku,
                Name = request.Name.Trim(),
                Price = request.Price,
                StockQuantity = request.StockQuantity,
                IsActive = request.IsActive
            };

            _context.Products.Add(product);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new ApiException(
                    409,
                    "A product with this SKU already exists.");
            }

            InvalidateProductCache();

            _logger.LogInformation(
                "Product created. ProductId: {ProductId}, SKU: {Sku}",
                product.Id,
                product.Sku);

            return MapToResponse(product);
        }

        // --------------------------------------------------
        // UPDATE PRODUCT WITH ROWVERSION
        // --------------------------------------------------

        public async Task<ProductResponseDto> UpdateProductAsync(
            int id,
            UpdateProductRequestDto request)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(x => x.Id == id);

            if (product == null)
            {
                throw new ApiException(
                    404,
                    "Product not found.");
            }

            byte[] clientRowVersion;

            try
            {
                clientRowVersion = Convert.FromBase64String(
                    request.RowVersion);
            }
            catch (FormatException)
            {
                throw new ApiException(
                    400,
                    "Invalid RowVersion format.");
            }

            if (clientRowVersion.Length != 8)
            {
                throw new ApiException(
                    400,
                    "Invalid RowVersion length.");
            }

            var sku = request.Sku.Trim().ToUpperInvariant();

            var duplicateSku = await _context.Products
                .AnyAsync(x => x.Id != id && x.Sku == sku);

            if (duplicateSku)
            {
                throw new ApiException(
                    409,
                    "Another product already uses this SKU.");
            }

            // Tell EF Core to compare the client's RowVersion
            // against the current SQL Server RowVersion.
            _context.Entry(product)
                .Property(x => x.RowVersion)
                .OriginalValue = clientRowVersion;

            product.Sku = sku;
            product.Name = request.Name.Trim();
            product.Price = request.Price;
            product.StockQuantity = request.StockQuantity;
            product.IsActive = request.IsActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning(
                    "Product update concurrency conflict. ProductId: {ProductId}",
                    id);

                throw new ApiException(
                    409,
                    "This product was modified by another request. " +
                    "Please reload the product and try again.");
            }
            catch (DbUpdateException ex)
                when (IsUniqueConstraintViolation(ex))
            {
                throw new ApiException(
                    409,
                    "Another product already uses this SKU.");
            }

            InvalidateProductCache();

            _logger.LogInformation(
                "Product updated. ProductId: {ProductId}",
                product.Id);

            return MapToResponse(product);
        }

        // --------------------------------------------------
        // SORTING
        // --------------------------------------------------

        private static IQueryable<Product> ApplySorting(
            IQueryable<Product> products,
            string sortBy,
            string sortDirection)
        {
            var descending = sortDirection.Equals(
                "desc",
                StringComparison.OrdinalIgnoreCase);

            return sortBy.ToLowerInvariant() switch
            {
                "name" => descending
                    ? products.OrderByDescending(x => x.Name)
                        .ThenBy(x => x.Id)
                    : products.OrderBy(x => x.Name)
                        .ThenBy(x => x.Id),

                "price" => descending
                    ? products.OrderByDescending(x => x.Price)
                        .ThenBy(x => x.Id)
                    : products.OrderBy(x => x.Price)
                        .ThenBy(x => x.Id),

                "stock" => descending
                    ? products.OrderByDescending(x => x.StockQuantity)
                        .ThenBy(x => x.Id)
                    : products.OrderBy(x => x.StockQuantity)
                        .ThenBy(x => x.Id),

                _ => products.OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
            };
        }

        // --------------------------------------------------
        // VALIDATION
        // --------------------------------------------------

        private static void ValidateQuery(ProductQueryDto query)
        {
            if (query.PageNumber < 1)
            {
                throw new ApiException(
                    400,
                    "PageNumber must be greater than zero.");
            }

            if (query.PageSize < 1 || query.PageSize > 100)
            {
                throw new ApiException(
                    400,
                    "PageSize must be between 1 and 100.");
            }

            if (query.MinPrice < 0 || query.MaxPrice < 0)
            {
                throw new ApiException(
                    400,
                    "Price filters cannot be negative.");
            }

            if (query.MinPrice.HasValue &&
                query.MaxPrice.HasValue &&
                query.MinPrice > query.MaxPrice)
            {
                throw new ApiException(
                    400,
                    "MinPrice cannot exceed MaxPrice.");
            }

            var allowedSortFields = new[]
            {
            "name",
            "price",
            "stock"
        };

            if (!allowedSortFields.Contains(
                query.SortBy,
                StringComparer.OrdinalIgnoreCase))
            {
                throw new ApiException(
                    400,
                    "Invalid SortBy. Allowed values: name, price, stock.");
            }

            if (string.IsNullOrWhiteSpace(query.SortDirection) ||
                (!query.SortDirection.Equals(
                    "asc",
                    StringComparison.OrdinalIgnoreCase) &&
                 !query.SortDirection.Equals(
                    "desc",
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ApiException(
                    400,
                    "SortDirection must be asc or desc.");
            }
        }

        // --------------------------------------------------
        // CACHE
        // --------------------------------------------------

        private string BuildCacheKey(ProductQueryDto query)
        {
            var version = _cache.GetOrCreate(
                "products:cache-version",
                entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromDays(1);

                    return Guid.NewGuid().ToString("N");
                });

            return
                $"products:{version}:" +
                $"{query.Search?.Trim().ToLowerInvariant()}:" +
                $"{query.MinPrice}:" +
                $"{query.MaxPrice}:" +
                $"{query.InStock}:" +
                $"{query.SortBy.ToLowerInvariant()}:" +
                $"{query.SortDirection.ToLowerInvariant()}:" +
                $"{query.PageNumber}:" +
                $"{query.PageSize}";
        }

        private void InvalidateProductCache()
        {
            // A versioned cache namespace invalidates all previous
            // product-list keys without maintaining an unbounded key list.
            _cache.Set(
                "products:cache-version",
                Guid.NewGuid().ToString("N"),
                TimeSpan.FromDays(1));
        }

        private static ProductResponseDto MapToResponse(Product product)
        {
            return new ProductResponseDto
            {
                Id = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                Price = product.Price,
                StockQuantity = product.StockQuantity,
                IsActive = product.IsActive,
                RowVersion = Convert.ToBase64String(product.RowVersion)
            };
        }

        private static bool IsUniqueConstraintViolation(
            DbUpdateException exception)
        {
            return exception.GetBaseException() is SqlException sqlException
                && sqlException.Number is 2601 or 2627;
        }
    }
}

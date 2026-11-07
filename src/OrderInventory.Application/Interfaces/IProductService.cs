using OrderInventory.Application.DTOs.Products;

namespace OrderInventory.Application.Interfaces
{
    public interface IProductService
    {
        Task<PagedResponseDto<ProductResponseDto>> GetProductsAsync(
            ProductQueryDto query);

        Task<ProductResponseDto> GetProductByIdAsync(int id);

        Task<ProductResponseDto> CreateProductAsync(
            CreateProductRequestDto request);

        Task<ProductResponseDto> UpdateProductAsync(
            int id,
            UpdateProductRequestDto request);
    }
}

using System.ComponentModel.DataAnnotations;

namespace OrderInventory.Application.DTOs.Products
{
    public class UpdateProductRequestDto
    {
        [Required]
        [StringLength(50, MinimumLength = 2)]
        public string Sku { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "9999999999999999")]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int StockQuantity { get; set; }

        public bool IsActive { get; set; }

        // Base64-encoded SQL Server RowVersion received from GET.
        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }
}

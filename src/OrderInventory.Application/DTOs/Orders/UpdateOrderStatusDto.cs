using System.ComponentModel.DataAnnotations;

namespace OrderInventory.Application.DTOs.Orders
{
    public class UpdateOrderStatusDto
    {
        [Required]
        public string Status { get; set; } = string.Empty;
    }
}

using System.ComponentModel.DataAnnotations;

namespace OrderInventory.Application.DTOs.Orders
{
    public class CreateOrderRequestDto
    {
        [Required]
        [MinLength(1)]
        [MaxLength(100)]
        public List<CreateOrderItemDto> Items { get; set; } = [];
    }
}

using System.ComponentModel.DataAnnotations;

namespace OrderInventory.Application.DTOs.Orders
{
    public class CreateOrderItemDto
    {
        [Range(1, int.MaxValue)]
        public int ProductId { get; set; }

        [Range(1, 10000)]
        public int Quantity { get; set; }
    }
}

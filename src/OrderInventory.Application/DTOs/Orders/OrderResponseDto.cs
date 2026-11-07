using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderInventory.Application.DTOs.Orders
{
    public class OrderResponseDto
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public decimal SubTotal { get; set; }

        public decimal Discount { get; set; }

        public decimal Total { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<OrderItemResponseDto> Items { get; set; } = [];
    }
}

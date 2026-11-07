using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderInventory.Domain.Entities
{
    public class Product
    {
        public int Id { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public int StockQuantity { get; set; }

        public bool IsActive { get; set; } = true;

        // SQL Server rowversion
        public byte[] RowVersion { get; set; } = [];

        public ICollection<OrderItem> OrderItems { get; set; }
            = new List<OrderItem>();
    }
}

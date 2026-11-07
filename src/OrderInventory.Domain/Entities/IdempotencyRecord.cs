using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderInventory.Domain.Entities
{
    public class IdempotencyRecord
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Key { get; set; } = string.Empty;

        public string RequestHash { get; set; } = string.Empty;

        public int? OrderId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;

        public Order? Order { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderInventory.Application.Interfaces
{
    public interface IProductCacheInvalidator
    {
        void Invalidate();
    }
}

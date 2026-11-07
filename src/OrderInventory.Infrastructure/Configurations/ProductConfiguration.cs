using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderInventory.Domain.Entities;

namespace OrderInventory.Infrastructure.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Sku)
                .HasMaxLength(50)
                .IsRequired();

            builder.HasIndex(x => x.Sku)
                .IsUnique();

            builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Price)
                .HasPrecision(18, 2);

            builder.Property(x => x.RowVersion)
                .IsRowVersion();

            builder.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_Products_Price",
                    "[Price] >= 0");

                t.HasCheckConstraint(
                    "CK_Products_StockQuantity",
                    "[StockQuantity] >= 0");
            });
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderInventory.Domain.Entities;

namespace OrderInventory.Infrastructure.Configurations
{
    public class IdempotencyRecordConfiguration
    : IEntityTypeConfiguration<IdempotencyRecord>
    {
        public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Key)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(x => x.RequestHash)
                .HasMaxLength(64)
                .IsRequired();

            builder.HasIndex(x => new
            {
                x.UserId,
                x.Key
            }).IsUnique();

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Order)
                .WithOne()
                .HasForeignKey<IdempotencyRecord>(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

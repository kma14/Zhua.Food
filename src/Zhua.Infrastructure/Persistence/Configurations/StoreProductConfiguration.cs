using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class StoreProductConfiguration : IEntityTypeConfiguration<StoreProduct>
{
    public void Configure(EntityTypeBuilder<StoreProduct> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceSku).HasMaxLength(100);
        b.Property(x => x.RawName).HasMaxLength(300);
        b.Property(x => x.RawBrand).HasMaxLength(150);
        b.Property(x => x.RawSize).HasMaxLength(50);
        b.Property(x => x.Gtin).HasMaxLength(20);
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.Property(x => x.UnitOfMeasure).HasMaxLength(20);
        b.Property(x => x.CurrentPrice).HasPrecision(10, 2);
        b.Property(x => x.CurrentSpecial).HasPrecision(10, 2);
        b.Property(x => x.UnitPrice).HasPrecision(12, 4);

        b.HasOne(x => x.Store)
            .WithMany(s => s.StoreProducts)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.CanonicalProduct)
            .WithMany(c => c.StoreProducts)
            .HasForeignKey(x => x.CanonicalProductId)
            .OnDelete(DeleteBehavior.SetNull);

        // A store's own SKU is unique within that store.
        b.HasIndex(x => new { x.StoreId, x.SourceSku }).IsUnique();
        b.HasIndex(x => x.CanonicalProductId);
        b.HasIndex(x => x.Gtin);
    }
}

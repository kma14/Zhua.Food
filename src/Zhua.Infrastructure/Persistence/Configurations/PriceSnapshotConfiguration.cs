using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class PriceSnapshotConfiguration : IEntityTypeConfiguration<PriceSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceSnapshot> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Price).HasPrecision(10, 2);
        b.Property(x => x.NonSpecialPrice).HasPrecision(10, 2);
        b.Property(x => x.UnitPrice).HasPrecision(12, 4);
        b.Property(x => x.Currency).HasMaxLength(3);

        b.HasOne(x => x.StoreProduct)
            .WithMany(sp => sp.PriceSnapshots)
            .HasForeignKey(x => x.StoreProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.CrawlRun)
            .WithMany(cr => cr.PriceSnapshots)
            .HasForeignKey(x => x.CrawlRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // Core history query: latest/over-time snapshots for a store product.
        b.HasIndex(x => new { x.StoreProductId, x.CapturedAt });
        b.HasIndex(x => x.CrawlRunId);
    }
}

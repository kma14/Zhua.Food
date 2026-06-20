using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class CrawlRunConfiguration : IEntityTypeConfiguration<CrawlRun>
{
    public void Configure(EntityTypeBuilder<CrawlRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);

        b.HasOne(x => x.Store)
            .WithMany(s => s.CrawlRuns)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.StoreId, x.StartedAt });
    }
}

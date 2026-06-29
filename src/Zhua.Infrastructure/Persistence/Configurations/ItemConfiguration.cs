using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.MatchKey).HasMaxLength(100);
        b.HasIndex(x => x.MatchKey).IsUnique().HasFilter("\"MatchKey\" IS NOT NULL"); // stable per-item key (D18)
        b.Property(x => x.Name).HasMaxLength(300);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Brand).HasMaxLength(150);
        b.Property(x => x.Size).HasMaxLength(50);
        b.Property(x => x.UnitOfMeasure).HasMaxLength(20);
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.Gtin).HasMaxLength(20);

        b.HasIndex(x => x.Category);
        b.HasIndex(x => x.Gtin);
        b.HasIndex(x => x.CategoryId); // browse/filter by the shared taxonomy (D22)
    }
}

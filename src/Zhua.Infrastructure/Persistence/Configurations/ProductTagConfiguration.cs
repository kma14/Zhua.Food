using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Chain).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Code).HasMaxLength(100);
        b.Property(x => x.Label).HasMaxLength(200);

        // Tag vocabulary is chain-specific (plan D13): one row per (Chain, Source, Code).
        b.HasIndex(x => new { x.Chain, x.Source, x.Code }).IsUnique();

        // Many-to-many with products. EF creates the join table.
        b.HasMany(x => x.Products).WithMany(p => p.Tags);
    }
}

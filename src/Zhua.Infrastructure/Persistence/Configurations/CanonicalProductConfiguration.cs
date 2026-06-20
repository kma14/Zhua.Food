using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class CanonicalProductConfiguration : IEntityTypeConfiguration<CanonicalProduct>
{
    public void Configure(EntityTypeBuilder<CanonicalProduct> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(300);
        b.Property(x => x.Brand).HasMaxLength(150);
        b.Property(x => x.Size).HasMaxLength(50);
        b.Property(x => x.UnitOfMeasure).HasMaxLength(20);
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.Gtin).HasMaxLength(20);

        b.HasIndex(x => x.Category);
        b.HasIndex(x => x.Gtin);
    }
}

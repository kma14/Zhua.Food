using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

/// <summary>Shared cross-store category tree (plan D22).</summary>
public class CanonicalCategoryConfiguration : IEntityTypeConfiguration<CanonicalCategory>
{
    public void Configure(EntityTypeBuilder<CanonicalCategory> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Path).HasMaxLength(600);
        b.Property(x => x.Slug).HasMaxLength(200);

        b.HasIndex(x => x.Path).IsUnique(); // stable upsert key across mapper re-runs

        // Self-referencing tree (Department → Aisle → Shelf). Restrict to avoid cascade cycles.
        b.HasOne(x => x.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ParentId);

        // Mapped store categories (D22): deleting a canonical node just unsets the link, never the store node.
        b.HasMany(x => x.StoreCategories)
            .WithOne(s => s.CanonicalCategory)
            .HasForeignKey(s => s.CanonicalCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Products under this canonical node: same — unset the link, don't delete the product.
        b.HasMany(x => x.Products)
            .WithOne(p => p.CanonicalCategory)
            .HasForeignKey(p => p.CanonicalCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

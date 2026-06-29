using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

/// <summary>Shared cross-store category tree (plan D22).</summary>
public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Path).HasMaxLength(600);
        b.Property(x => x.Slug).HasMaxLength(200);

        b.HasIndex(x => x.Path).IsUnique(); // stable upsert key across mapper re-runs
        b.Property(x => x.IsArchived).HasDefaultValue(false); // soft-delete (D25 phase 3)

        // Self-referencing tree (Department → Aisle → Shelf). Restrict to avoid cascade cycles.
        b.HasOne(x => x.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ParentId);

        // Mapped store categories (D22): deleting a item node just unsets the link, never the store node.
        b.HasMany(x => x.StoreCategories)
            .WithOne(s => s.Category)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Products under this item node: same — unset the link, don't delete the product.
        b.HasMany(x => x.Products)
            .WithOne(p => p.CategoryNode)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

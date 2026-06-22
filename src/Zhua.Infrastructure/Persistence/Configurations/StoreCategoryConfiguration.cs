using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class StoreCategoryConfiguration : IEntityTypeConfiguration<StoreCategory>
{
    public void Configure(EntityTypeBuilder<StoreCategory> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ExternalId).HasMaxLength(50);
        b.Property(x => x.Slug).HasMaxLength(150);
        b.Property(x => x.Name).HasMaxLength(200);

        b.HasOne(x => x.Store)
            .WithMany(s => s.StoreCategories)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referencing tree (Department → Aisle → Shelf). Restrict to avoid cascade cycles.
        b.HasOne(x => x.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.Kind, x.ExternalId }).IsUnique();
        b.HasIndex(x => x.ParentId);

        // Many-to-many: a product appears under several categories (plan D11). EF creates the join table.
        b.HasMany(x => x.Products).WithMany(p => p.Categories);
    }
}

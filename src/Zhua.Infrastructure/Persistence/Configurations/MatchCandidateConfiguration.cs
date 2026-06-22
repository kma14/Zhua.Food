using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class MatchCandidateConfiguration : IEntityTypeConfiguration<MatchCandidate>
{
    public void Configure(EntityTypeBuilder<MatchCandidate> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(300);

        b.HasOne(x => x.StoreProduct)
            .WithMany()
            .HasForeignKey(x => x.StoreProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.CanonicalProduct)
            .WithMany()
            .HasForeignKey(x => x.CanonicalProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // One decision per (product, canonical) pair — lets the matcher upsert and skip answered pairs (D18).
        b.HasIndex(x => new { x.StoreProductId, x.CanonicalProductId }).IsUnique();
        b.HasIndex(x => x.Status);
    }
}

using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;

namespace Zhua.Infrastructure.Persistence;

public class ZhuaDbContext(DbContextOptions<ZhuaDbContext> options) : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<CanonicalProduct> CanonicalProducts => Set<CanonicalProduct>();
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<CrawlRun> CrawlRuns => Set<CrawlRun>();
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();
    public DbSet<CanonicalCategory> CanonicalCategories => Set<CanonicalCategory>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<MatchCandidate> MatchCandidates => Set<MatchCandidate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZhuaDbContext).Assembly);
    }
}

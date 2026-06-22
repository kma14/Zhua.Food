using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Infrastructure.Persistence.Configurations;

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Chain).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Suburb).HasMaxLength(100);
        b.Property(x => x.ExternalStoreId).HasMaxLength(100);
        b.HasIndex(x => x.Chain);

        // Milestone-1 store seed (plan §1). Lat/long = geolocation used to select the store on the source site.
        b.HasData(
            new Store { Id = StoreSeed.WoolworthsTakapuna, Chain = Chain.Woolworths, Name = "Woolworths Takapuna", Suburb = "Takapuna", Latitude = -36.7879, Longitude = 174.7695, IsActive = true },
            // New World "Takapuna" = the Shore City branch; ExternalStoreId pins the Foodstuffs store id (D15).
            new Store { Id = StoreSeed.NewWorldTakapuna, Chain = Chain.NewWorld, Name = "New World Takapuna", Suburb = "Takapuna", Latitude = -36.7868, Longitude = 174.7731, ExternalStoreId = "60928d93-06fa-4d8f-92a6-8c359e7e846d", IsActive = true },
            // PAK'nSAVE Albany (online); the nearer Wairau Valley branch is in-store-only. storeId resolved at crawl time by geolocation.
            new Store { Id = StoreSeed.PaknSaveAlbany, Chain = Chain.PaknSave, Name = "PAK'nSAVE Albany", Suburb = "Albany", Latitude = -36.7228, Longitude = 174.7005, IsActive = true });
    }
}

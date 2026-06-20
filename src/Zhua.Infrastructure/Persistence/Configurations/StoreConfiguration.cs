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
            new Store { Id = StoreSeed.NewWorldTakapuna, Chain = Chain.NewWorld, Name = "New World Takapuna", Suburb = "Takapuna", Latitude = -36.7868, Longitude = 174.7731, IsActive = true },
            new Store { Id = StoreSeed.PaknSaveGlenfield, Chain = Chain.PaknSave, Name = "PAK'nSAVE Glenfield", Suburb = "Glenfield", Latitude = -36.7783, Longitude = 174.7447, IsActive = true });
    }
}

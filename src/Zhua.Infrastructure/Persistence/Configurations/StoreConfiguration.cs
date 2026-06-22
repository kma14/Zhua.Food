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
        // 3 branches per chain (plan §1 / D16). Foodstuffs storeId pinned via ExternalStoreId; Woolworths by geolocation.
        b.HasData(
            // Woolworths (national pricing) — selected by geolocation lat/long.
            new Store { Id = StoreSeed.WoolworthsTakapuna, Chain = Chain.Woolworths, Name = "Woolworths Takapuna", Suburb = "Takapuna", Latitude = -36.7879, Longitude = 174.7695, IsActive = true },
            new Store { Id = StoreSeed.WoolworthsGlenfield, Chain = Chain.Woolworths, Name = "Woolworths Glenfield", Suburb = "Glenfield", Latitude = -36.7807, Longitude = 174.7228, IsActive = true },
            new Store { Id = StoreSeed.WoolworthsBrownsBay, Chain = Chain.Woolworths, Name = "Woolworths Browns Bay", Suburb = "Browns Bay", Latitude = -36.7166, Longitude = 174.7466, IsActive = true },
            // New World (Foodstuffs) — storeId pinned (D15). "Metro" = the CBD store originally mis-seeded as Takapuna.
            new Store { Id = StoreSeed.NewWorldMetro, Chain = Chain.NewWorld, Name = "New World Metro Auckland", Suburb = "Auckland Central", Latitude = -36.8464, Longitude = 174.7659, ExternalStoreId = "60928d93-06fa-4d8f-92a6-8c359e7e846d", IsActive = true },
            new Store { Id = StoreSeed.NewWorldShoreCity, Chain = Chain.NewWorld, Name = "New World Shore City", Suburb = "Takapuna", Latitude = -36.7876, Longitude = 174.7700, ExternalStoreId = "1898a189-acf3-4320-8704-7a9cc6b3924d", IsActive = true },
            new Store { Id = StoreSeed.NewWorldBrownsBay, Chain = Chain.NewWorld, Name = "New World Browns Bay", Suburb = "Browns Bay", Latitude = -36.7160, Longitude = 174.7473, ExternalStoreId = "dbdfdd2a-55f7-4870-9b51-979286323647", IsActive = true },
            // PAK'nSAVE (Foodstuffs) — storeId pinned. North Shore online = Albany only; Botany + Highland Park (East) are most Chinese-dense.
            new Store { Id = StoreSeed.PaknSaveAlbany, Chain = Chain.PaknSave, Name = "PAK'nSAVE Albany", Suburb = "Albany", Latitude = -36.7300, Longitude = 174.7067, ExternalStoreId = "65defcf2-bc15-490e-a84f-1f13b769cd22", IsActive = true },
            new Store { Id = StoreSeed.PaknSaveBotany, Chain = Chain.PaknSave, Name = "PAK'nSAVE Botany", Suburb = "Botany", Latitude = -36.9307, Longitude = 174.9130, ExternalStoreId = "60561e46-ece7-43a7-b142-9b14812586e4", IsActive = true },
            new Store { Id = StoreSeed.PaknSaveHighlandPark, Chain = Chain.PaknSave, Name = "PAK'nSAVE Highland Park", Suburb = "Highland Park", Latitude = -36.8989, Longitude = 174.9047, ExternalStoreId = "2a1b331a-fc4a-496a-b072-e97cc8f70cae", IsActive = true });
    }
}

namespace Zhua.Infrastructure.Persistence;

/// <summary>
/// Stable seed identifiers for the Milestone-1 stores (plan §1) — 3 branches per chain so we can compare
/// same-brand branch prices (Foodstuffs stores are independently owned/priced; Woolworths is national).
/// </summary>
public static class StoreSeed
{
    // Woolworths (national pricing — branch differences are minimal). Store selected by geolocation.
    public static readonly Guid WoolworthsTakapuna  = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid WoolworthsGlenfield = new("11111111-1111-1111-1111-111111111112");
    public static readonly Guid WoolworthsBrownsBay = new("11111111-1111-1111-1111-111111111113");

    // New World (Foodstuffs — independently priced per store). storeId pinned via ExternalStoreId.
    public static readonly Guid NewWorldMetro     = new("22222222-2222-2222-2222-222222222222"); // existing row, relabelled
    public static readonly Guid NewWorldShoreCity = new("22222222-2222-2222-2222-222222222223");
    public static readonly Guid NewWorldBrownsBay = new("22222222-2222-2222-2222-222222222224");

    // PAK'nSAVE (Foodstuffs — independently priced per store). North Shore has only Albany online;
    // Botany + Highland Park are the most Chinese-dense online stores (East Auckland).
    public static readonly Guid PaknSaveAlbany       = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid PaknSaveBotany       = new("33333333-3333-3333-3333-333333333334");
    public static readonly Guid PaknSaveHighlandPark = new("33333333-3333-3333-3333-333333333335");
}

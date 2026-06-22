namespace Zhua.Infrastructure.Persistence;

/// <summary>Stable seed identifiers for the Milestone-1 stores (plan §1).</summary>
public static class StoreSeed
{
    public static readonly Guid WoolworthsTakapuna = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid NewWorldTakapuna = new("22222222-2222-2222-2222-222222222222");
    // PAK'nSAVE: M1 store is Albany — the nearer Wairau Valley branch is in-store-only (no online catalog).
    public static readonly Guid PaknSaveAlbany = new("33333333-3333-3333-3333-333333333333");
}

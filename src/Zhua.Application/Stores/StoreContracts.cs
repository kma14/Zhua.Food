namespace Zhua.Application.Stores;

/// <summary>A physical store the app tracks prices for (active stores only).</summary>
public sealed record StoreView(
    Guid Id,
    string Supermarket,      // Woolworths | NewWorld | PaknSave
    string Name,             // the store's display name, e.g. "PAK'nSAVE Albany"
    string Suburb,
    double Latitude,
    double Longitude,
    int ProductCount,                  // priced listings we currently hold for this store
    DateTimeOffset? LastCrawledAt);    // when this store last finished a successful crawl (freshness)

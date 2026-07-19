namespace Zhua.Domain.Enums;

/// <summary>
/// The banner a <see cref="Entities.Store"/> trades under — and, for us, its <b>crawler family</b>: the crawl
/// orchestrator picks an <c>IStoreCrawler</c> by the store's <c>Chain</c>, so one value = one independent crawler.
/// Exposed to the front-end as <c>supermarket</c> (the API renamed the field for shoppers; the internal name stays
/// <c>Chain</c> — a common retail/English term that also implies "an independent crawler system" for us). One value
/// per banner/platform, <b>not</b> per owner: New World + PAK'nSAVE are both Foodstuffs but separate values;
/// Woolworths + FreshChoice are both Woolworths NZ but separate values (different storefront platforms).
/// </summary>
public enum Chain
{
    Woolworths = 1,
    NewWorld = 2,
    PaknSave = 3,
    FreshChoice = 4,   // Woolworths NZ banner, but its own MyFoodLink storefront → its own crawler
}

using Zhua.Domain.Enums;

namespace Zhua.Crawling.Foodstuffs;

/// <summary>New World crawler — Foodstuffs platform (plan D15); departments come from the base.</summary>
public sealed class NewWorldCrawler : FoodstuffsCrawler
{
    public override Chain Chain => Chain.NewWorld;
    protected override string SiteBaseUrl => "https://www.newworld.co.nz";
    protected override string ApiBaseUrl => "https://api-prod.newworld.co.nz";
}

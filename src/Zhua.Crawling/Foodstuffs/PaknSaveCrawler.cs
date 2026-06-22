using Zhua.Domain.Enums;

namespace Zhua.Crawling.Foodstuffs;

/// <summary>PAK'nSAVE crawler — same Foodstuffs platform as New World (plan D15); departments come from the base.</summary>
public sealed class PaknSaveCrawler : FoodstuffsCrawler
{
    public override Chain Chain => Chain.PaknSave;
    protected override string SiteBaseUrl => "https://www.paknsave.co.nz";
    protected override string ApiBaseUrl => "https://api-prod.paknsave.co.nz";
}

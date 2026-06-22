using Zhua.Domain.Enums;

namespace Zhua.Application.Ingestion;

/// <summary>A promo/marketing tag on a scraped product (plan D13), e.g. Woolworths tagType "IsSpecial".</summary>
public sealed record ScrapedTag(ProductTagSource Source, string Code, string? Label = null);

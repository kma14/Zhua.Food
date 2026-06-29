using Zhua.Domain.Enums;

namespace Zhua.Application.Crawling;

/// <summary>One node of a crawled category path (Department / Aisle / Shelf) for a scraped product (plan D11).</summary>
public sealed record ScrapedCategoryNode(CategoryKind Kind, string ExternalId, string Slug, string Name);

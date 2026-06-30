namespace Zhua.Application.Products;

/// <summary>A product's item link after a PATCH.</summary>
public sealed record ProductLinkView(Guid Id, Guid? ItemId);

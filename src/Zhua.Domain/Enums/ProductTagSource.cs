namespace Zhua.Domain.Enums;

/// <summary>
/// Which slot of the source's promo metadata a <see cref="Entities.ProductTag"/> came from (plan D13).
/// Woolworths exposes a primary <c>productTag.tagType</c> enum and a free-text <c>additionalTag</c>; keeping
/// the source lets the two namespaces coexist without colliding.
/// </summary>
public enum ProductTagSource
{
    /// <summary>The primary promo badge, e.g. Woolworths <c>tagType</c> (IsSpecial / IsGreatPrice / IsClubPrice …).</summary>
    Primary = 1,

    /// <summary>A secondary marketing tag, e.g. Woolworths <c>additionalTag.name</c> (Clearance / Organic / own-brand …).</summary>
    Additional = 2,
}

namespace Zhua.Domain.Enums;

/// <summary>
/// The normalised kind of promotion currently on a <see cref="Entities.Product"/>
/// (docs/internals/promotions-model.md, decided 2026-07-17). One primary type per product (decision D1);
/// when a source carries several signals the precedence is MemberPrice &gt; Special &gt; Multibuy.
/// </summary>
public enum PromoType
{
    None = 0,

    /// <summary>Public temporary special — the shelf price itself is discounted. The only type <c>/deals</c> surfaces (decision C).</summary>
    Special = 1,

    /// <summary>Loyalty-card price (Woolworths Everyday Rewards / New World Clubcard). <c>CurrentPrice</c> stays the
    /// non-member shelf price; the card price lives in <c>MemberPrice</c>.</summary>
    MemberPrice = 2,

    /// <summary>"N for $X" — the unit shelf price is unaffected; the deal is the quantity+total pair.</summary>
    Multibuy = 3,
}

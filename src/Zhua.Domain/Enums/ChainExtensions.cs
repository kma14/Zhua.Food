namespace Zhua.Domain.Enums;

public static class ChainExtensions
{
    /// <summary>
    /// The chain's loyalty program — the card a shopper must hold for a <see cref="PromoType.MemberPrice"/> deal
    /// (docs/internals/promotions-model.md). Null = the chain runs no program: PAK'nSAVE deliberately has none
    /// (EDLP positioning — every promo is public); FreshChoice has shown none in recon so far.
    /// </summary>
    public static string? LoyaltyProgram(this Chain chain) => chain switch
    {
        Chain.Woolworths => "Everyday Rewards",
        Chain.NewWorld => "New World Clubcard",
        _ => null,
    };
}

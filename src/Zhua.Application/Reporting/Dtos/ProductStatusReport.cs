namespace Zhua.Application.Reporting.Dtos;

/// <summary>
/// The product-status distribution as a single table (D30.1): one <see cref="ChainStatusRow"/> per supermarket plus a
/// grand <see cref="Total"/> row. Every active-store listing falls into exactly one status, so a row's columns sum to
/// its <see cref="ChainStatusRow.Total"/>. Statuses follow the glossary (docs/internals/glossary.md): the first four
/// are "linked to an item" (aggregated, by which chain anchors the item), the last two are "unmatched"
/// (待审商品 / 悬空商品).
/// </summary>
public sealed record ProductStatusReport(IReadOnlyList<ChainStatusRow> Chains, ChainStatusRow Total);

/// <summary>One supermarket's product counts by match status (or the grand total when <see cref="Supermarket"/> is
/// "Total"). Internal report — items are never shown to shoppers (D25); this counts listings by how the matcher
/// placed them.</summary>
public sealed record ChainStatusRow(
    string Supermarket,
    int FoodstuffsItem,   // ① linked to a foodstuffs: item
    int WoolworthsItem,   // ② linked to a woolworths: item
    int FreshChoiceItem,  // ③ linked to a freshchoice: singleton item
    int ManualItem,       // linked to a manual: item (hand-created / other scheme)
    int PendingReview,    // ④ 待审商品 — unlinked, has a pending review candidate
    int Held,             // ⑤ 悬空商品 — unlinked, no candidate (guard-held / unmatchable for now)
    int Total);

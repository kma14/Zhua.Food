using System.Text.RegularExpressions;

namespace Zhua.Domain.Matching;

/// <summary>
/// Normalisation + similarity helpers for cross-store product matching (plan D18). Pure/testable — no I/O. Lives in
/// Domain because it encodes the matching rules the <see cref="Zhua.Domain.Services.IItemMatchingPolicy"/> scores by.
/// Cross-chain names differ wildly (Woolworths "mainland cheese colby" vs Foodstuffs "Smooth &amp; Creamy Colby
/// Cheese"), so we match on <c>brand + size</c> (hard filter) then score by name-token overlap.
/// </summary>
public static partial class ProductNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "with", "of", "in", "for", "to", "or", "no", "nz",
    };

    /// <summary>lowercase, trimmed, punctuation-stripped brand. Null/blank → null.</summary>
    public static string? NormalizeBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return null;
        var cleaned = NonAlnum().Replace(brand.ToLowerInvariant(), " ").Trim();
        cleaned = Spaces().Replace(cleaned, " ");
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// Normalised size for equality, e.g. "1 kg" → "1kg". Returns null for non-fixed sizes (weight/each, e.g.
    /// "kg"/"ea") — those are sold loose and can't be matched on size.
    /// </summary>
    public static string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return null;
        var s = size.ToLowerInvariant().Replace(" ", "");
        if (!s.Any(char.IsDigit)) return null; // no number = "kg"/"ea" (loose) → unmatchable by size
        // Canonicalise multipack units so the same pack matches across chains: Woolworths writes "12pack",
        // Foodstuffs (and our FreshChoice extraction) write "12pk", and packets appear as "pkt" — all mean the
        // same count. Without this, an egg 12-pack at Woolworths ("12pack") never matched the same at
        // FreshChoice/Foodstuffs ("12pk"). "pk" itself is already canonical.
        return PackUnit().Replace(s, "pk");
    }

    /// <summary>Significant name tokens (lowercased), with the brand, stop-words, sizes and noise removed.</summary>
    public static HashSet<string> Tokenize(string? name, string? brand)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(name)) return set;

        var brandTokens = NormalizeBrand(brand)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var brandSet = new HashSet<string>(brandTokens, StringComparer.Ordinal);

        foreach (var raw in NonAlnum().Split(name.ToLowerInvariant()))
        {
            var t = raw.Trim();
            if (t.Length < 2) continue;                 // single chars / empties
            if (StopWords.Contains(t)) continue;
            if (brandSet.Contains(t)) continue;         // the brand is matched separately
            if (SizeToken().IsMatch(t)) continue;       // "250g", "1kg", "6pk" embedded in the name
            if (t.All(char.IsDigit)) continue;          // bare numbers
            set.Add(t);
        }
        return set;
    }

    /// <summary>Overlap coefficient |A∩B| / min(|A|,|B|) in [0,1]. 0 when either side is empty.</summary>
    public static double TokenOverlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count <= b.Count ? a.Count(b.Contains) : b.Count(a.Contains);
        return (double)inter / Math.Min(a.Count, b.Count);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Fresh-produce matching (2026-07-24). Unbranded / weight-sold fresh produce + butcher cuts have no brand and a
    // loose size, so the brand+size hard filter (Tier 2) drops them and each chain anchors its own item — the same
    // broccoli splits into 2-3 items. For this "fresh regime" we match on a canonical produce NAME instead: strip the
    // noise (the category word a retailer stuffs into the brand field, the private-label root, marketing filler,
    // units) but KEEP every discriminating word (organic/premium/snacking/cut/variety), then require an EXACT
    // token-set match within the same coarse fresh department. Design + how the word lists were derived (and how to
    // extend them for a new chain): docs/internals/matching.md § Fresh-produce matching.

    /// <summary>Pseudo-brands: a category word the retailer dumps in the brand field (Woolworths "fresh vegetable" =
    /// "this is a vegetable", not a brand). Zero cross-store identity → dropped, same as an empty brand. "herb"/"herbs"
    /// is a pseudo-brand ONLY in the produce department (a real descriptor in meat, e.g. "lamb leg with herb"), so it
    /// is handled by <see cref="NormalizeProduceName"/> against the department, not listed here.</summary>
    private static readonly HashSet<string> PseudoBrandWords = new(StringComparer.Ordinal)
    {
        "fresh", "vegetable", "vegetables", "fruit", "fruits", "produce", "instore", "deli",
    };

    /// <summary>Private-label roots: a retailer's own house brand. A real brand string, but the SAME raw produce
    /// carries a different one at each chain ("Woolworths" broccoli vs "Pams" broccoli), so it has no cross-store
    /// identity for fresh produce. Matched on the FIRST brand token so "woolworths nz" / "macro organic" both count.
    /// Applies ONLY inside the fresh regime — for packaged goods a private label is a real discriminator.</summary>
    private static readonly HashSet<string> PrivateLabelRoots = new(StringComparer.Ordinal)
    {
        "woolworths", "macro", "essentials", "ww", "pams", "value", "homebrand", "countdown", "signature",
    };

    /// <summary>Extra filler dropped from a produce name (on top of <see cref="StopWords"/> + embedded sizes):
    /// pack-form and provenance words that carry no produce identity.</summary>
    private static readonly HashSet<string> ProduceNoiseWords = new(StringComparer.Ordinal)
    {
        "new", "zealand", "each", "ea", "per", "min", "order", "approx", "approximately",
        "loose", "prepacked", "prepack", "pack", "packed", "bag", "bagged", "pkt", "packet", "punnet",
    };

    /// <summary>How discriminating a listing's brand is for the fresh regime.</summary>
    public enum FreshBrandKind { Real, Empty, Pseudo, PrivateLabel }

    /// <summary>Coarse, cross-chain-stable fresh department (chains name it differently: "Meat, Poultry &amp; Seafood"
    /// vs "Meat" + "Seafood"). Also decides the herb rule. <see cref="None"/> = not a fresh department.</summary>
    public enum FreshDept { None, Produce, Protein }

    /// <summary>Classify a listing's brand for the fresh regime — anything but <see cref="FreshBrandKind.Real"/> is
    /// treated as noise (an eligible fresh line). A real third-party brand means it's a packaged good → brand+size.</summary>
    public static FreshBrandKind ClassifyFreshBrand(string? brand)
    {
        var nb = NormalizeBrand(brand);
        if (nb is null) return FreshBrandKind.Empty;
        var tokens = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0 && PrivateLabelRoots.Contains(tokens[0])) return FreshBrandKind.PrivateLabel;
        if (tokens.All(t => PseudoBrandWords.Contains(t) || t is "herb" or "herbs")) return FreshBrandKind.Pseudo;
        return FreshBrandKind.Real;
    }

    /// <summary>Fresh-regime brand test: empty / pseudo / private-label brands carry no cross-store identity for
    /// produce, so the line is eligible for name-based matching; a real brand keeps it on the brand+size path.</summary>
    public static bool IsProduceEligibleBrand(string? brand) => ClassifyFreshBrand(brand) is not FreshBrandKind.Real;

    /// <summary>A weight-sold / loose size (no fixed pack): null/blank, "kg"/"ea"/"per kg", or a "min order …" / "loose"
    /// note. A real pack size (250g, 1.5L, 6pack) is a packaged good and stays on the brand+size path.</summary>
    public static bool IsLooseSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return true;
        var s = size.ToLowerInvariant();
        if (s.Contains("per kg") || s.Contains("min order") || s.Contains("loose") || s.Contains("each")) return true;
        return NormalizeSize(size) is null; // no fixed number ⇒ "kg"/"ea" ⇒ loose
    }

    /// <summary>Map a store's department name to the coarse fresh bucket (or <see cref="FreshDept.None"/>).</summary>
    public static FreshDept ClassifyFreshDept(string? departmentName)
    {
        if (string.IsNullOrWhiteSpace(departmentName)) return FreshDept.None;
        var d = departmentName.ToLowerInvariant();
        if (d.Contains("fruit") || d.Contains("veg")) return FreshDept.Produce;
        if (d.Contains("meat") || d.Contains("poultry") || d.Contains("seafood") || d.Contains("fish")) return FreshDept.Protein;
        return FreshDept.None;
    }

    /// <summary>
    /// The canonical produce identity of a listing name within a fresh department: significant tokens with pseudo-brand
    /// / private-label / filler / size words removed and the rest singularised + sorted, so word order and store wording
    /// don't matter. Discriminating words (organic, premium, snacking, cut, variety) are KEPT — the match is EXACT
    /// token-set equality, so "broccoli" ≠ "broccoli head" ≠ "organic broccoli" (those stay separate / go to review,
    /// never auto-merge). "herb"/"herbs" is dropped only when <paramref name="dept"/> is <see cref="FreshDept.Produce"/>.
    /// Returns null for a non-fresh department or a name that reduces to nothing.
    /// </summary>
    public static string? NormalizeProduceName(string? name, FreshDept dept)
    {
        if (dept == FreshDept.None || string.IsNullOrWhiteSpace(name)) return null;
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in NonAlnum().Split(name.ToLowerInvariant()))
        {
            var t = raw.Trim();
            if (t.Length < 2) continue;
            if (t.All(char.IsDigit)) continue;
            if (SizeToken().IsMatch(t)) continue;
            if (StopWords.Contains(t) || ProduceNoiseWords.Contains(t)) continue;
            if (PseudoBrandWords.Contains(t) || PrivateLabelRoots.Contains(t)) continue;
            if (t is "herb" or "herbs" && dept == FreshDept.Produce) continue; // pseudo-brand only in produce
            set.Add(Singularize(t));
        }
        return set.Count == 0 ? null : string.Join(' ', set);
    }

    /// <summary>Conservative singularisation for produce tokens: drop a trailing "s" (carrots→carrot) but not "-ss"
    /// (cress) and only when it leaves a real stem (len &gt; 3). Naive by design — rare mis-stems (greens→green) are
    /// harmless under exact-set matching (both sides stem the same way).</summary>
    private static string Singularize(string t) =>
        t.Length > 3 && t[^1] == 's' && t[^2] != 's' ? t[..^1] : t;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex("^[0-9]+(\\.[0-9]+)?(g|kg|ml|l|pk|pack|ea|cm|mm|pc|pcs)$")]
    private static partial Regex SizeToken();

    [GeneratedRegex("(packs?|pkts?)")]
    private static partial Regex PackUnit();
}

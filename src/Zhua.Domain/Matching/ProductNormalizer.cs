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

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex("^[0-9]+(\\.[0-9]+)?(g|kg|ml|l|pk|pack|ea|cm|mm|pc|pcs)$")]
    private static partial Regex SizeToken();

    [GeneratedRegex("(packs?|pkts?)")]
    private static partial Regex PackUnit();
}

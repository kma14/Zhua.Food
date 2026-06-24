namespace Zhua.Application.Pricing;

/// <summary>
/// Normalises a store's published unit price to ONE comparable base per measure family, so products can be
/// ranked by value across differing pack sizes: weight → per kg, volume → per L, count → per each.
/// Returns null when the unit of measure can't be parsed (e.g. blank or "100"), so callers sort those last.
/// </summary>
public static class UnitPriceNormalizer
{
    /// <param name="unitPrice">Price per <paramref name="unitOfMeasure"/> as the store published it.</param>
    /// <param name="unitOfMeasure">e.g. "1kg", "100g", "100ml", "1L", "1ea".</param>
    /// <returns>(price in the base unit, base unit label "1kg"/"1L"/"1ea"), or null if not normalisable.</returns>
    public static (decimal Price, string Unit)? ToComparable(decimal? unitPrice, string? unitOfMeasure)
    {
        if (unitPrice is not { } p || string.IsNullOrWhiteSpace(unitOfMeasure)) return null;

        var uom = unitOfMeasure.Trim().ToLowerInvariant();

        // Split a leading quantity (default 1) from the unit suffix: "100g" → 100 + "g", "kg" → 1 + "kg".
        var i = 0;
        while (i < uom.Length && (char.IsDigit(uom[i]) || uom[i] == '.')) i++;
        var qty = i == 0 ? 1m : decimal.TryParse(uom[..i], out var q) ? q : 1m;
        var unit = uom[i..].Trim();
        if (qty <= 0) return null;

        return unit switch
        {
            "kg" => (p / qty, "1kg"),
            "g" => (p / qty * 1000m, "1kg"),
            "l" => (p / qty, "1L"),
            "ml" => (p / qty * 1000m, "1L"),
            "ea" => (p / qty, "1ea"),
            _ => null, // unknown/blank unit (e.g. "100") — not comparable
        };
    }
}

using Xunit;
using Zhua.Application.Pricing;

namespace Zhua.Ingestion.Tests;

public class UnitPriceNormalizerTests
{
    [Theory]
    [InlineData(22.48, "1kg", 22.48, "1kg")]   // already per kg
    [InlineData(8.26, "100g", 82.60, "1kg")]   // per 100g → ×10 per kg
    [InlineData(5.00, "10g", 500.00, "1kg")]   // per 10g → ×100 per kg
    [InlineData(2.50, "1L", 2.50, "1L")]
    [InlineData(0.30, "100ml", 3.00, "1L")]    // per 100ml → ×10 per L
    [InlineData(0.30, "100mL", 3.00, "1L")]    // case-insensitive
    [InlineData(0.85, "1ea", 0.85, "1ea")]
    [InlineData(0.85, "ea", 0.85, "1ea")]
    public void Normalises_to_comparable_base(decimal unitPrice, string uom, decimal expectedPrice, string expectedUnit)
    {
        var r = UnitPriceNormalizer.ToComparable(unitPrice, uom);
        Assert.NotNull(r);
        Assert.Equal(expectedPrice, decimal.Round(r!.Value.Price, 2));
        Assert.Equal(expectedUnit, r.Value.Unit);
    }

    [Theory]
    [InlineData("")]      // blank uom
    [InlineData("100")]   // no unit suffix
    [InlineData("pack")]  // unknown unit
    public void Returns_null_for_uncomparable_uom(string uom)
        => Assert.Null(UnitPriceNormalizer.ToComparable(5.00m, uom));

    [Fact]
    public void Returns_null_when_unit_price_missing()
        => Assert.Null(UnitPriceNormalizer.ToComparable(null, "1kg"));
}

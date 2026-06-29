using Zhua.Application.Matching;

namespace Zhua.Crawling.Tests;

/// <summary>Unit tests for the cross-store matching normalisation/similarity (plan D18).</summary>
public class ProductNormalizerTests
{
    [Theory]
    [InlineData("Mainland", "mainland")]
    [InlineData("  Anchor  ", "anchor")]
    [InlineData("PAK'nSAVE", "pak nsave")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void NormalizeBrand_lowercases_and_strips(string? input, string? expected)
        => Assert.Equal(expected, ProductNormalizer.NormalizeBrand(input));

    [Theory]
    [InlineData("250g", "250g")]
    [InlineData("1 kg", "1kg")]
    [InlineData("4 x 100ml", "4x100ml")]
    [InlineData("kg", null)]   // loose / by-weight → unmatchable on size
    [InlineData("ea", null)]
    [InlineData(null, null)]
    public void NormalizeSize_fixes_sizes_and_nulls_loose(string? input, string? expected)
        => Assert.Equal(expected, ProductNormalizer.NormalizeSize(input));

    [Fact]
    public void Tokenize_drops_brand_size_and_stopwords()
    {
        // Woolworths style: brand-prefixed.
        Assert.Equal(new[] { "cheese", "colby" }.OrderBy(x => x),
            ProductNormalizer.Tokenize("mainland cheese colby", "mainland").OrderBy(x => x));

        // size token + brand removed.
        Assert.Equal(new[] { "blue", "milk" }.OrderBy(x => x),
            ProductNormalizer.Tokenize("Anchor Blue Milk 2L", "Anchor").OrderBy(x => x));

        // Foodstuffs marketing style.
        Assert.Equal(new[] { "cheese", "colby", "creamy", "smooth" }.OrderBy(x => x),
            ProductNormalizer.Tokenize("Smooth & Creamy Colby Cheese", null).OrderBy(x => x));
    }

    [Fact]
    public void TokenOverlap_uses_overlap_coefficient()
    {
        var woolworths = ProductNormalizer.Tokenize("mainland cheese colby", "mainland");
        var foodstuffs = ProductNormalizer.Tokenize("Smooth & Creamy Colby Cheese", null);
        Assert.Equal(1.0, ProductNormalizer.TokenOverlap(woolworths, foodstuffs)); // {colby,cheese} / min(2,4)=2

        // "mainland butter" is ambiguous against salted/unsalted — both score 1.0 (→ review, not auto-link).
        var butter = ProductNormalizer.Tokenize("mainland butter", "mainland");
        Assert.Equal(1.0, ProductNormalizer.TokenOverlap(butter, ProductNormalizer.Tokenize("Salted Butter", null)));
        Assert.Equal(1.0, ProductNormalizer.TokenOverlap(butter, ProductNormalizer.Tokenize("Unsalted Butter", null)));

        Assert.Equal(0.0, ProductNormalizer.TokenOverlap(butter, []));
    }
}

using Zhua.Domain.Matching;

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
    [InlineData("12 Pack", "12pk")]   // Woolworths multipack form ≡ Foodstuffs/FreshChoice "12pk"
    [InlineData("12pack", "12pk")]
    [InlineData("12pk", "12pk")]
    [InlineData("6packs", "6pk")]
    [InlineData("10pkt", "10pk")]
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

    // --- Fresh-produce matching (2026-07-24) ---

    [Theory]
    [InlineData("fresh vegetable broccoli head", ProductNormalizer.FreshDept.Produce, "broccoli head")]
    [InlineData("Broccoli", ProductNormalizer.FreshDept.Produce, "broccoli")]
    [InlineData("woolworths broccolini bag", ProductNormalizer.FreshDept.Produce, "broccolini")]
    [InlineData("Macro Organic Fresh Vegetable Carrots", ProductNormalizer.FreshDept.Produce, "carrot organic")] // keeps organic
    [InlineData("Carrots", ProductNormalizer.FreshDept.Produce, "carrot")]                                       // singularised
    [InlineData("Beef Mince", ProductNormalizer.FreshDept.Protein, "beef mince")]
    [InlineData("NZ Premium Beef Mince", ProductNormalizer.FreshDept.Protein, "beef mince premium")]             // keeps premium
    [InlineData("Fresh Herb Coriander", ProductNormalizer.FreshDept.Produce, "coriander")]                       // herb=pseudo in produce
    [InlineData("Lamb Leg with Herb", ProductNormalizer.FreshDept.Protein, "herb lamb leg")]                     // herb KEPT in meat
    [InlineData("Fresh Vegetable", ProductNormalizer.FreshDept.Produce, null)]                                   // all noise → null
    [InlineData("Broccoli", ProductNormalizer.FreshDept.None, null)]                                             // not fresh → null
    public void NormalizeProduceName_strips_noise_keeps_discriminators(string name, ProductNormalizer.FreshDept dept, string? expected)
        => Assert.Equal(expected, ProductNormalizer.NormalizeProduceName(name, dept));

    [Fact]
    public void NormalizeProduceName_is_word_order_independent()
        => Assert.Equal(
            ProductNormalizer.NormalizeProduceName("broccoli head", ProductNormalizer.FreshDept.Produce),
            ProductNormalizer.NormalizeProduceName("head broccoli", ProductNormalizer.FreshDept.Produce));

    [Fact]
    public void NormalizeProduceName_keeps_distinct_cuts_apart()
    {
        // The safety property: exact token-set, so "broccoli" ≠ "broccoli head" and "chicken wing" ≠ "chicken breast"
        // never auto-merge (they resolve to different canonical keys → stay separate / go to review).
        var p = ProductNormalizer.FreshDept.Produce;
        Assert.NotEqual(ProductNormalizer.NormalizeProduceName("Broccoli", p),
                        ProductNormalizer.NormalizeProduceName("Broccoli Head", p));
        Assert.NotEqual(ProductNormalizer.NormalizeProduceName("Broccoli", p),
                        ProductNormalizer.NormalizeProduceName("Organic Broccoli", p));
        var m = ProductNormalizer.FreshDept.Protein;
        Assert.NotEqual(ProductNormalizer.NormalizeProduceName("Chicken Wing", m),
                        ProductNormalizer.NormalizeProduceName("Chicken Breast", m));
    }

    [Theory]
    [InlineData("fresh vegetable", ProductNormalizer.FreshBrandKind.Pseudo)]
    [InlineData("fresh herb", ProductNormalizer.FreshBrandKind.Pseudo)]
    [InlineData("produce", ProductNormalizer.FreshBrandKind.Pseudo)]
    [InlineData(null, ProductNormalizer.FreshBrandKind.Empty)]
    [InlineData("", ProductNormalizer.FreshBrandKind.Empty)]
    [InlineData("Woolworths", ProductNormalizer.FreshBrandKind.PrivateLabel)]
    [InlineData("Woolworths NZ", ProductNormalizer.FreshBrandKind.PrivateLabel)]
    [InlineData("Macro Organic", ProductNormalizer.FreshBrandKind.PrivateLabel)]
    [InlineData("Pams", ProductNormalizer.FreshBrandKind.PrivateLabel)]
    [InlineData("Hellers", ProductNormalizer.FreshBrandKind.Real)]
    [InlineData("The Odd Bunch", ProductNormalizer.FreshBrandKind.Real)] // decision ①: fuzzy sub-brands stay Real for v1
    [InlineData("Superb Herb", ProductNormalizer.FreshBrandKind.Real)]
    public void ClassifyFreshBrand_buckets_brand(string? brand, ProductNormalizer.FreshBrandKind expected)
        => Assert.Equal(expected, ProductNormalizer.ClassifyFreshBrand(brand));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("kg", true)]
    [InlineData("per kg", true)]
    [InlineData("ea", true)]
    [InlineData("min order 250g", true)]
    [InlineData("loose", true)]
    [InlineData("250g", false)]
    [InlineData("1.5L", false)]
    [InlineData("6pack", false)]
    public void IsLooseSize_detects_weight_sold(string? size, bool expected)
        => Assert.Equal(expected, ProductNormalizer.IsLooseSize(size));

    [Theory]
    [InlineData("Fruit & Vegetables", ProductNormalizer.FreshDept.Produce)]
    [InlineData("Fruit & Veg", ProductNormalizer.FreshDept.Produce)]
    [InlineData("Meat, Poultry & Seafood", ProductNormalizer.FreshDept.Protein)]
    [InlineData("Meat & Poultry", ProductNormalizer.FreshDept.Protein)]
    [InlineData("Fish & Seafood", ProductNormalizer.FreshDept.Protein)]
    [InlineData("Frozen", ProductNormalizer.FreshDept.None)]
    [InlineData("Fridge & Deli", ProductNormalizer.FreshDept.None)]
    [InlineData(null, ProductNormalizer.FreshDept.None)]
    public void ClassifyFreshDept_buckets_department(string? name, ProductNormalizer.FreshDept expected)
        => Assert.Equal(expected, ProductNormalizer.ClassifyFreshDept(name));
}

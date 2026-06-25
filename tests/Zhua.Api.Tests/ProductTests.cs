using System.Net;
using System.Net.Http.Json;
using Zhua.Api.Contracts;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class ProductTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_finds_by_name_with_cheapest_and_image()
    {
        var items = await _client.GetFromJsonAsync<List<ProductSummary>>("/products/search?q=mince");

        Assert.NotNull(items);
        var mince = items!.Single(i => i.Id == TestData.BeefMince);
        Assert.Equal(11.00m, mince.CheapestPrice);          // MIN across stores
        Assert.Equal(3, mince.StoreCount);
        Assert.NotNull(mince.ImageUrl);
    }

    [Fact]
    public async Task Search_requires_q_else_400()
    {
        var res = await _client.GetAsync("/products/search");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Search_storeId_filter_recomputes_within_store()
    {
        var items = await _client.GetFromJsonAsync<List<ProductSummary>>(
            $"/products/search?q=mince&storeId={StoreSeed.WoolworthsTakapuna}");

        var mince = items!.Single(i => i.Id == TestData.BeefMince);
        Assert.Equal(12.00m, mince.CheapestPrice);          // Woolworths price, not the global 11.00
        Assert.Equal(1, mince.StoreCount);
    }

    [Fact]
    public async Task Products_by_category_matches_the_subresource()
    {
        var viaProducts = await _client.GetFromJsonAsync<List<CategoryProduct>>(
            $"/products?category={TestData.AisleBeef}");
        Assert.Equal(2, viaProducts!.Count);
    }

    [Fact]
    public async Task Products_without_category_is_400()
    {
        var res = await _client.GetAsync("/products");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Compare_lists_every_store_cheapest_first()
    {
        var c = await _client.GetFromJsonAsync<ProductComparison>($"/products/{TestData.BeefMince}");

        Assert.NotNull(c);
        Assert.Equal(3, c!.Prices.Count);
        Assert.Equal("PAK'nSAVE Albany", c.Prices[0].Store); // cheapest first
        Assert.Equal(11.00m, c.CheapestPrice);
        Assert.Equal(2.50m, c.Saving);                       // 13.50 − 11.00
        Assert.NotNull(c.ImageUrl);                          // representative
        Assert.All(c.Prices, p => Assert.NotNull(p.ImageUrl));
    }

    [Fact]
    public async Task Compare_unknown_product_is_404()
    {
        var res = await _client.GetAsync($"/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Price_history_returns_a_per_store_step_series()
    {
        var h = await _client.GetFromJsonAsync<ProductPriceHistory>(
            $"/products/{TestData.BeefMince}/price-history");

        Assert.NotNull(h);
        var pak = h!.Stores.Single(s => s.Store == "PAK'nSAVE Albany");
        Assert.Equal(2, pak.Points.Count);
        Assert.Equal(11.50m, pak.Points[0].Price);           // ordered by CapturedAt
        Assert.Equal(11.00m, pak.Points[1].Price);
    }

    [Fact]
    public async Task Price_history_unknown_product_is_404()
    {
        var res = await _client.GetAsync($"/products/{Guid.NewGuid()}/price-history");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

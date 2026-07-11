using System.Net;
using System.Net.Http.Json;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class ProductTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_groups_listings_by_item_with_all_stores()
    {
        var groups = (await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?q=mince"))?.Items;

        Assert.NotNull(groups);
        var mince = groups!.Single(g => g.ItemId == TestData.BeefMince);
        Assert.Equal("beef mince (grouped)", mince.Description);   // item caption (D25)
        Assert.Equal(3, mince.Products.Count);                     // all three store listings, no aggregation
        Assert.Equal(TestData.MincePak, mince.Products[0].Id);     // ordered cheapest-first by default
        Assert.Equal(11.00m, mince.Products[0].Price);
        var ww = mince.Products.Single(p => p.Supermarket == "Woolworths");
        Assert.True(ww.IsOnSpecial);
        Assert.Equal(15.00m, ww.WasPrice);

        // Store-first search also surfaces UNMATCHED listings (a group of one).
        Assert.Contains(groups!, g => g.ItemId is null && g.Products.Single().Name == "Beef Mince Premium 1kg");
    }

    [Fact]
    public async Task Bare_products_returns_the_catalogue_paginated()
    {
        var groups = (await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?size=5"))?.Items;
        Assert.NotNull(groups);
        Assert.NotEmpty(groups!);                                  // no longer 400s without a filter
        Assert.True(groups!.Count <= 5);
    }

    [Fact]
    public async Task List_is_a_paged_sorted_envelope()
    {
        var env = await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?size=1&sort=priceAsc");
        Assert.NotNull(env);
        Assert.Equal(1, env!.Page);
        Assert.Equal(1, env.Size);
        Assert.Single(env.Items);
        Assert.True(env.Total >= 1);
        Assert.Equal((int)Math.Ceiling(env.Total / 1.0), env.TotalPages);
        Assert.Equal(env.Page < env.TotalPages, env.HasMore);      // more pages after this one-item page
        Assert.Equal("priceAsc", env.Sort);                        // echoes the applied sort

        // Sorting happens server-side over the whole set before paging: nameAsc → non-decreasing names.
        var byName = await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?sort=nameAsc&size=100");
        var names = byName!.Items.Select(g => g.Description ?? g.Products[0].Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);

        // Unknown sort falls back to the default, echoed back.
        var def = await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?size=5&sort=bogus");
        Assert.Equal("unitPriceAsc", def!.Sort);
    }

    [Fact]
    public async Task Search_storeId_filter_scopes_to_that_store()
    {
        var groups = (await _client.GetFromJsonAsync<PagedResult<ProductGroup>>(
            $"/products?q=mince&storeId={StoreSeed.WoolworthsTakapuna}"))?.Items;

        var mince = groups!.Single(g => g.ItemId == TestData.BeefMince);
        Assert.Single(mince.Products);                            // only the Woolworths listing
        Assert.Equal(12.00m, mince.Products[0].Price);
        Assert.Equal("Woolworths Takapuna", mince.Products[0].Store);
    }

    [Fact]
    public async Task Products_by_category_matches_the_subresource()
    {
        var viaProducts = (await _client.GetFromJsonAsync<PagedResult<ProductGroup>>(
            $"/products?category={TestData.AisleBeef}"))?.Items;
        Assert.Equal(2, viaProducts!.Count);                     // mince group + eye fillet
        Assert.Contains(viaProducts!, g => g.ItemId == TestData.BeefMince);
    }

    [Fact]
    public async Task Detail_returns_the_group_with_every_store_cheapest_first()
    {
        var g = await _client.GetFromJsonAsync<ProductGroup>($"/products/{TestData.MincePak}");

        Assert.NotNull(g);
        Assert.Equal(TestData.BeefMince, g!.ItemId);
        Assert.Equal("beef mince (grouped)", g.Description);
        Assert.Equal(3, g.Products.Count);
        Assert.Equal("PAK'nSAVE Albany", g.Products[0].Store);   // cheapest first
        Assert.Equal(11.00m, g.Products[0].Price);
        Assert.All(g.Products, p => Assert.NotNull(p.ImageUrl));
    }

    [Fact]
    public async Task Detail_unknown_product_is_404()
    {
        var res = await _client.GetAsync($"/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Price_history_returns_a_per_store_step_series()
    {
        var h = await _client.GetFromJsonAsync<ProductPriceHistory>(
            $"/products/{TestData.MincePak}/price-history");

        Assert.NotNull(h);
        var pak = h!.Stores.Single(s => s.Store == "PAK'nSAVE Albany");
        Assert.Equal(2, pak.Points.Count);
        Assert.Equal(11.50m, pak.Points[0].Price);               // ordered by CapturedAt
        Assert.Equal(11.00m, pak.Points[1].Price);
    }

    [Fact]
    public async Task Price_history_unknown_product_is_404()
    {
        var res = await _client.GetAsync($"/products/{Guid.NewGuid()}/price-history");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

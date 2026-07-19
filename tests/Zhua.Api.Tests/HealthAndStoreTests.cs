using System.Net;
using System.Net.Http.Json;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class HealthAndStoreTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Health_db_reports_up()
    {
        var res = await _client.GetAsync("/health/db");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Stores_returns_active_stores_with_metadata()
    {
        var stores = await _client.GetFromJsonAsync<List<StoreView>>("/stores");

        Assert.NotNull(stores);
        // 8 active (1 Woolworths + 3 New World + 3 PAK'nSAVE + 1 FreshChoice, D26); 2 extra WW branches inactive.
        Assert.Equal(8, stores!.Count);
        Assert.DoesNotContain(stores, s => s.Name.Contains("Glenfield"));

        var pak = stores.Single(s => s.Name == "PAK'nSAVE Albany");
        Assert.Equal("PaknSave", pak.Supermarket);
        Assert.Equal("Albany", pak.Suburb);
        Assert.True(pak.ProductCount >= 1);
        Assert.NotNull(pak.LastCrawledAt); // it has a successful CrawlRun in the seed

        var fc = stores.Single(s => s.Supermarket == "FreshChoice");
        Assert.Equal("FreshChoice Hauraki Corner", fc.Name);
        Assert.Equal("Hauraki", fc.Suburb);
    }

    [Fact]
    public async Task Stores_filter_by_supermarket()
    {
        var pak = await _client.GetFromJsonAsync<List<StoreView>>("/stores?supermarket=PaknSave");

        Assert.NotNull(pak);
        Assert.Equal(3, pak!.Count);
        Assert.All(pak, s => Assert.Equal("PaknSave", s.Supermarket));
    }

    [Fact]
    public async Task Stores_unknown_supermarket_is_400()
    {
        var res = await _client.GetAsync("/stores?supermarket=Foo");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

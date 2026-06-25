using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zhua.Api.Contracts;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class DealAndAdminTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Deals_returns_specials_with_was_price_and_saving()
    {
        var deals = await _client.GetFromJsonAsync<List<DealItem>>("/deals");

        Assert.NotNull(deals);
        var mince = deals!.Single(d => d.Store == "Woolworths Takapuna");
        Assert.Equal("Woolworths", mince.Supermarket);
        Assert.Equal(12.00m, mince.Price);
        Assert.Equal(15.00m, mince.WasPrice);
        Assert.Equal(3.00m, mince.Saving);
        Assert.NotNull(mince.ImageUrl);
    }

    [Fact]
    public async Task Deals_filter_by_supermarket()
    {
        var nw = await _client.GetFromJsonAsync<List<DealItem>>("/deals?supermarket=NewWorld");
        Assert.Empty(nw!); // no New World specials in the seed

        var ww = await _client.GetFromJsonAsync<List<DealItem>>("/deals?supermarket=Woolworths");
        Assert.Contains(ww!, d => d.Store == "Woolworths Takapuna");
    }

    [Fact]
    public async Task Deals_unknown_supermarket_is_400()
    {
        var res = await _client.GetAsync("/deals?supermarket=Nope");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Match_candidates_lists_pending_queue()
    {
        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/admin/match-candidates");

        Assert.NotNull(items);
        Assert.Contains(items!, m => m.Id == TestData.CandidateForList);
        var c = items!.Single(m => m.Id == TestData.CandidateForList);
        Assert.Equal("Beef Mince Premium 1kg", c.StoreProductName);
        Assert.Equal("Woolworths", c.Supermarket);
        Assert.NotEqual(Guid.Empty, c.StoreProductId);        // exposed for the link/create-canonical actions
        Assert.Equal(TestData.MatchTarget, c.CandidateCanonicalId);
    }

    [Fact]
    public async Task Link_canonical_links_the_listing_and_clears_its_candidates()
    {
        var res = await _client.PostAsJsonAsync(
            $"/admin/store-products/{TestData.LinkTargetSp}/link-canonical",
            new LinkCanonicalRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var queue = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/admin/match-candidates");
        Assert.DoesNotContain(queue!, m => m.Id == TestData.CandidateOnLinkTarget);
    }

    [Fact]
    public async Task Link_canonical_unknown_store_product_is_404()
    {
        var res = await _client.PostAsJsonAsync(
            $"/admin/store-products/{Guid.NewGuid()}/link-canonical",
            new LinkCanonicalRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Link_canonical_unknown_canonical_is_404()
    {
        var res = await _client.PostAsJsonAsync(
            $"/admin/store-products/{TestData.LinkTargetSp}/link-canonical",
            new LinkCanonicalRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_canonical_mints_a_new_canonical_links_it_and_clears_candidates()
    {
        var res = await _client.PostAsJsonAsync(
            $"/admin/store-products/{TestData.CreateTargetSp}/create-canonical",
            new CreateCanonicalRequest(null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var newId = (await res.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("canonicalProductId").GetGuid();
        Assert.NotEqual(Guid.Empty, newId);

        // The new canonical defaults its name from the listing, so it's now findable + linked.
        var hits = await _client.GetFromJsonAsync<List<ProductSummary>>("/products/search?q=create me");
        var created = hits!.Single(p => p.Id == newId);
        Assert.Equal(6.00m, created.CheapestPrice);

        var queue = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/admin/match-candidates");
        Assert.DoesNotContain(queue!, m => m.Id == TestData.CandidateOnCreateTarget);
    }

    [Fact]
    public async Task Create_canonical_unknown_store_product_is_404()
    {
        var res = await _client.PostAsJsonAsync(
            $"/admin/store-products/{Guid.NewGuid()}/create-canonical",
            new CreateCanonicalRequest(null, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Approve_links_the_match_and_removes_it_from_the_queue()
    {
        var res = await _client.PostAsync($"/admin/match-candidates/{TestData.CandidateToApprove}/approve", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/admin/match-candidates");
        Assert.DoesNotContain(items!, m => m.Id == TestData.CandidateToApprove);
    }

    [Fact]
    public async Task Approve_unknown_is_404()
    {
        var res = await _client.PostAsync($"/admin/match-candidates/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reject_marks_the_candidate_rejected()
    {
        var res = await _client.PostAsync($"/admin/match-candidates/{TestData.CandidateToReject}/reject", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/admin/match-candidates");
        Assert.DoesNotContain(items!, m => m.Id == TestData.CandidateToReject);
    }
}

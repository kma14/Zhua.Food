using System.Net;
using System.Net.Http.Json;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class DealAndAdminTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Deals_returns_specials_with_was_price_and_saving()
    {
        var deals = (await _client.GetFromJsonAsync<PagedResult<DealItem>>("/deals"))?.Items;

        Assert.NotNull(deals);
        var mince = deals!.Single(d => d.Store == "Woolworths Takapuna");
        Assert.Equal("Woolworths", mince.Supermarket);
        Assert.Equal(12.00m, mince.Price);
        Assert.Equal(15.00m, mince.WasPrice);
        Assert.Equal(3.00m, mince.Saving);
        Assert.NotNull(mince.ImageUrl);
    }

    [Fact]
    public async Task Deals_include_current_specials_with_no_was_price()
    {
        var deals = (await _client.GetFromJsonAsync<PagedResult<DealItem>>("/deals"))?.Items;
        var noWas = deals!.Single(d => d.Sku == "pns-special-nowas");
        Assert.Equal("PaknSave", noWas.Supermarket);
        Assert.Equal(8.99m, noWas.Price);
        Assert.Null(noWas.WasPrice);   // Foodstuffs first-seen-on-special: no recoverable regular price yet
        Assert.Null(noWas.Saving);     // so no computed saving — but still surfaced as a current promotion
    }

    [Fact]
    public async Task Deals_filter_by_supermarket()
    {
        var nw = (await _client.GetFromJsonAsync<PagedResult<DealItem>>("/deals?supermarket=NewWorld"))?.Items;
        Assert.Empty(nw!); // no New World specials in the seed

        var ww = (await _client.GetFromJsonAsync<PagedResult<DealItem>>("/deals?supermarket=Woolworths"))?.Items;
        Assert.Contains(ww!, d => d.Store == "Woolworths Takapuna");
    }

    [Fact]
    public async Task Deals_filter_by_category_and_store()
    {
        // Category filter mirrors /products: the Beef aisle subtree → only the on-special beef mince (Woolworths),
        // NOT the unmatched salmon special (it has no item/category). Unknown category → 404.
        var beef = (await _client.GetFromJsonAsync<PagedResult<DealItem>>(
            $"/deals?category={TestData.AisleBeef}"))?.Items;
        Assert.Contains(beef!, d => d.Store == "Woolworths Takapuna");
        Assert.DoesNotContain(beef!, d => d.Sku == "pns-special-nowas");

        var unknown = await _client.GetAsync($"/deals?category={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Store filter: only deals at the given store. The salmon special is at PAK'nSAVE Botany.
        var botany = (await _client.GetFromJsonAsync<PagedResult<DealItem>>(
            $"/deals?storeId={StoreSeed.PaknSaveBotany}"))?.Items;
        Assert.All(botany!, d => Assert.Equal("PAK'nSAVE Botany", d.Store));
        Assert.Contains(botany!, d => d.Sku == "pns-special-nowas");

        // supermarket + storeId intersect: PAK store + Woolworths chain → empty.
        var none = (await _client.GetFromJsonAsync<PagedResult<DealItem>>(
            $"/deals?storeId={StoreSeed.PaknSaveBotany}&supermarket=Woolworths"))?.Items;
        Assert.Empty(none!);
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
        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/match-candidates");

        Assert.NotNull(items);
        Assert.Contains(items!, m => m.Id == TestData.CandidateForList);
        var c = items!.Single(m => m.Id == TestData.CandidateForList);
        Assert.Equal("Beef Mince Premium 1kg", c.ProductName);
        Assert.Equal("Woolworths", c.Supermarket);
        Assert.NotEqual(Guid.Empty, c.ProductId);        // exposed so the UI can PATCH /products/{id}
        Assert.Equal(TestData.MatchTarget, c.CandidateItemId);
    }

    [Fact]
    public async Task Patch_store_product_links_an_existing_item_and_clears_its_candidates()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/products/{TestData.LinkTargetSp}",
            new UpdateProductLinkRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var link = await res.Content.ReadFromJsonAsync<ProductLinkView>();
        Assert.Equal(TestData.MatchTarget, link!.ItemId);

        var queue = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/match-candidates");
        Assert.DoesNotContain(queue!, m => m.Id == TestData.CandidateOnLinkTarget);
    }

    [Fact]
    public async Task Patch_store_product_unknown_listing_is_404()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/products/{Guid.NewGuid()}",
            new UpdateProductLinkRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Patch_store_product_unknown_item_is_404()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/products/{TestData.LinkTargetSp}",
            new UpdateProductLinkRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_item_then_link_makes_a_new_findable_linked_product()
    {
        // 1. Create the item from fields (the review UI pre-fills these from the listing it's on).
        var create = await _client.PostAsJsonAsync("/items",
            new CreateItemRequest("Create Me 500g", null, "Acme", "500g", null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var item = await create.Content.ReadFromJsonAsync<ItemView>();
        Assert.NotEqual(Guid.Empty, item!.Id);
        Assert.Equal("Create Me 500g", item.Description);   // owned label defaults to the name

        // 2. Link the listing to it via the same PATCH everything else uses.
        var link = await _client.PatchAsJsonAsync(
            $"/products/{TestData.CreateTargetSp}",
            new UpdateProductLinkRequest(item.Id));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);

        // It's now findable (store-first search on the listing's real name) + priced, and its candidate is gone.
        var hits = (await _client.GetFromJsonAsync<PagedResult<ProductGroup>>("/products?q=create me"))?.Items;
        var created = hits!.Single(g => g.ItemId == item.Id);
        Assert.Equal(TestData.CreateTargetSp, created.Products.Single().Id);  // the linked listing
        Assert.Equal(6.00m, created.Products.Single().Price);

        var queue = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/match-candidates");
        Assert.DoesNotContain(queue!, m => m.Id == TestData.CandidateOnCreateTarget);
    }

    [Fact]
    public async Task Create_item_blank_name_is_400()
    {
        var res = await _client.PostAsJsonAsync("/items",
            new CreateItemRequest("   ", null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Patch_candidate_approved_links_the_match_and_removes_it_from_the_queue()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/match-candidates/{TestData.CandidateToApprove}",
            new UpdateMatchCandidateRequest("approved"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var decision = await res.Content.ReadFromJsonAsync<MatchCandidateDecision>();
        Assert.Equal("Approved", decision!.Status);
        Assert.Equal(TestData.MatchTarget, decision.ItemId);

        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/match-candidates");
        Assert.DoesNotContain(items!, m => m.Id == TestData.CandidateToApprove);
    }

    [Fact]
    public async Task Patch_candidate_unknown_is_404()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/match-candidates/{Guid.NewGuid()}",
            new UpdateMatchCandidateRequest("approved"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Patch_candidate_bad_status_is_400()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/match-candidates/{TestData.CandidateForList}",
            new UpdateMatchCandidateRequest("maybe"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Patch_candidate_rejected_marks_the_candidate_rejected()
    {
        var res = await _client.PatchAsJsonAsync(
            $"/match-candidates/{TestData.CandidateToReject}",
            new UpdateMatchCandidateRequest("rejected"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var items = await _client.GetFromJsonAsync<List<MatchCandidateView>>("/match-candidates");
        Assert.DoesNotContain(items!, m => m.Id == TestData.CandidateToReject);
    }
}

using System.Net;
using System.Net.Http.Json;

namespace Zhua.Api.Tests;

/// <summary>Admin item-merge correction (rework phase 4) — POST /items/{id}/merge.</summary>
[Collection(ApiCollection.Name)]
public class MergeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Merge_repoints_products_redirects_the_source_and_is_idempotent()
    {
        // Merge From → Into: the one listing moves to the survivor.
        var res = await _client.PostAsJsonAsync(
            $"/items/{TestData.MergeFromItem}/merge", new MergeItemRequest(TestData.MergeIntoItem));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var view = await res.Content.ReadFromJsonAsync<ItemMergeView>();
        Assert.Equal(TestData.MergeIntoItem, view!.SurvivorId);
        Assert.Equal(1, view.ProductsMoved);

        // The survivor's group now holds BOTH listings.
        var group = await _client.GetFromJsonAsync<ProductGroup>($"/products/{TestData.MergeIntoSp}");
        Assert.Equal(TestData.MergeIntoItem, group!.ItemId);
        Assert.Contains(group.Products, p => p.Id == TestData.MergeFromSp);
        Assert.Contains(group.Products, p => p.Id == TestData.MergeIntoSp);

        // The merged-away item is no longer a valid link target (a link would be undone next matcher run).
        var relink = await _client.PatchAsJsonAsync(
            $"/products/{TestData.MergeFromSp}", new UpdateProductLinkRequest(TestData.MergeFromItem));
        Assert.Equal(HttpStatusCode.NotFound, relink.StatusCode);

        // Re-merging into the same survivor is a no-op success (idempotent).
        var again = await _client.PostAsJsonAsync(
            $"/items/{TestData.MergeFromItem}/merge", new MergeItemRequest(TestData.MergeIntoItem));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<ItemMergeView>())!.ProductsMoved);

        // Merging the survivor back into the tombstone would be a redirect cycle → 400.
        var cycle = await _client.PostAsJsonAsync(
            $"/items/{TestData.MergeIntoItem}/merge", new MergeItemRequest(TestData.MergeFromItem));
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
    }

    [Fact]
    public async Task Merge_into_self_is_400()
    {
        var res = await _client.PostAsJsonAsync(
            $"/items/{TestData.MatchTarget}/merge", new MergeItemRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Merge_unknown_source_is_404()
    {
        var res = await _client.PostAsJsonAsync(
            $"/items/{Guid.NewGuid()}/merge", new MergeItemRequest(TestData.MatchTarget));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Merge_unknown_target_is_404()
    {
        var res = await _client.PostAsJsonAsync(
            $"/items/{TestData.MatchTarget}/merge", new MergeItemRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

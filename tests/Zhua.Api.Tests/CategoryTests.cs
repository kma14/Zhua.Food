using System.Net;
using System.Net.Http.Json;
using Zhua.Api.Contracts;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class CategoryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Tree_returns_nested_nodes_with_subtree_totals()
    {
        var tree = await _client.GetFromJsonAsync<List<CategoryNode>>("/categories");

        Assert.NotNull(tree);
        var meat = tree!.Single(n => n.Id == TestData.DeptMeat);
        Assert.Equal("Department", meat.Kind);
        Assert.Equal(3, meat.TotalProductCount);            // Beef(mince+fillet) + Chicken

        var beef = meat.Children.Single(n => n.Id == TestData.AisleBeef);
        Assert.Equal(2, beef.TotalProductCount);            // mince (shelf) + eye fillet (aisle)
        Assert.Contains(beef.Children, n => n.Id == TestData.ShelfBeefMince);
    }

    [Fact]
    public async Task Tree_kind_department_caps_depth_but_keeps_totals()
    {
        var tree = await _client.GetFromJsonAsync<List<CategoryNode>>("/categories?kind=department");

        var meat = tree!.Single(n => n.Id == TestData.DeptMeat);
        Assert.Empty(meat.Children);             // trimmed
        Assert.Equal(3, meat.TotalProductCount); // but the whole subtree is still counted
    }

    [Fact]
    public async Task Tree_storeId_filter_scopes_counts_to_that_store()
    {
        // Beef Mince is at PAK'nSAVE Albany; the Eye Fillet is only at PAK'nSAVE Botany.
        var albany = await _client.GetFromJsonAsync<List<CategoryNode>>(
            $"/categories?storeId={StoreSeed.PaknSaveAlbany}");
        var botany = await _client.GetFromJsonAsync<List<CategoryNode>>(
            $"/categories?storeId={StoreSeed.PaknSaveBotany}");
        var both = await _client.GetFromJsonAsync<List<CategoryNode>>(
            $"/categories?storeId={StoreSeed.PaknSaveAlbany}&storeId={StoreSeed.PaknSaveBotany}");

        Assert.Equal(1, Beef(albany!).TotalProductCount);
        Assert.Equal(1, Beef(botany!).TotalProductCount);
        Assert.Equal(2, Beef(both!).TotalProductCount);

        static CategoryNode Beef(List<CategoryNode> t) =>
            t.Single(n => n.Id == TestData.DeptMeat).Children.Single(n => n.Id == TestData.AisleBeef);
    }

    [Fact]
    public async Task Category_products_merge_across_stores_at_cheapest()
    {
        var items = await _client.GetFromJsonAsync<List<CategoryProduct>>(
            $"/categories/{TestData.AisleBeef}/products");

        Assert.NotNull(items);
        Assert.Equal(2, items!.Count); // mince (shelf) + eye fillet (aisle) — whole subtree

        var mince = items.Single(i => i.Id == TestData.BeefMince);
        Assert.Equal(11.00m, mince.CheapestPrice);
        Assert.Equal("beef mince (grouped)", mince.Description); // owned grouping label (D25)
        Assert.Equal("PAK'nSAVE Albany", mince.CheapestStore);
        Assert.Equal("PaknSave", mince.Supermarket);
        Assert.Equal(3, mince.StoreCount);
        Assert.True(mince.OnSpecialSomewhere);             // Woolworths has it on special
        Assert.NotNull(mince.ImageUrl);
    }

    [Fact]
    public async Task Category_products_storeId_filter_scopes_to_store()
    {
        var items = await _client.GetFromJsonAsync<List<CategoryProduct>>(
            $"/categories/{TestData.AisleBeef}/products?storeId={StoreSeed.PaknSaveAlbany}");

        Assert.NotNull(items);
        Assert.Single(items!);                              // only the mince is at Albany
        Assert.Equal(TestData.BeefMince, items![0].Id);
        Assert.Equal("PAK'nSAVE Albany", items[0].CheapestStore);
        Assert.Equal(1, items[0].StoreCount);
    }

    [Fact]
    public async Task Category_products_unknown_category_is_404()
    {
        var res = await _client.GetAsync($"/categories/{Guid.NewGuid()}/products");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

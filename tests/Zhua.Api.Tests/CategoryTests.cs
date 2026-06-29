using System.Net;
using System.Net.Http.Json;
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
    public async Task Category_products_group_listings_across_stores()
    {
        var groups = await _client.GetFromJsonAsync<List<ProductGroup>>(
            $"/categories/{TestData.AisleBeef}/products");

        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count); // mince (shelf) + eye fillet (aisle) — whole subtree

        var mince = groups.Single(g => g.ItemId == TestData.BeefMince);
        Assert.Equal("beef mince (grouped)", mince.Description); // item grouping caption (D25)
        Assert.Equal(3, mince.Products.Count);
        Assert.Equal("PAK'nSAVE Albany", mince.Products[0].Store);   // cheapest first
        Assert.Equal(11.00m, mince.Products[0].Price);
        Assert.Contains(mince.Products, p => p.IsOnSpecial);    // Woolworths has it on special
    }

    [Fact]
    public async Task Category_products_storeId_filter_scopes_to_store()
    {
        var groups = await _client.GetFromJsonAsync<List<ProductGroup>>(
            $"/categories/{TestData.AisleBeef}/products?storeId={StoreSeed.PaknSaveAlbany}");

        Assert.NotNull(groups);
        Assert.Single(groups!);                             // only the mince is at Albany
        Assert.Equal(TestData.BeefMince, groups![0].ItemId);
        Assert.Single(groups[0].Products);
        Assert.Equal("PAK'nSAVE Albany", groups[0].Products[0].Store);
    }

    [Fact]
    public async Task Category_products_unknown_category_is_404()
    {
        var res = await _client.GetAsync($"/categories/{Guid.NewGuid()}/products");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

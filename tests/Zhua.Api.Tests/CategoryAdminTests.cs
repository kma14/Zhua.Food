using System.Net;
using System.Net.Http.Json;
using Zhua.Api.Contracts;

namespace Zhua.Api.Tests;

[Collection(ApiCollection.Name)]
public class CategoryAdminTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<List<CategoryNode>> TreeAsync() =>
        (await _client.GetFromJsonAsync<List<CategoryNode>>("/categories"))!;

    private static CategoryNode? Find(IEnumerable<CategoryNode> nodes, Guid id)
    {
        foreach (var n in nodes)
        {
            if (n.Id == id) return n;
            var hit = Find(n.Children, id);
            if (hit is not null) return hit;
        }
        return null;
    }

    [Fact]
    public async Task Create_adds_a_category_to_the_tree()
    {
        var res = await _client.PostAsJsonAsync("/categories",
            new CreateCategoryRequest("Shelf", "Frozen Yoghurt", TestData.AisleIceCream));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<CategorySummary>();
        Assert.Equal("frozen/ice-cream-desserts/frozen-yoghurt", created!.Path);  // derived path
        Assert.Equal(TestData.AisleIceCream, created.ParentId);

        Assert.NotNull(Find(await TreeAsync(), created.Id)); // now visible in the tree
    }

    [Fact]
    public async Task Create_unknown_kind_is_400()
    {
        var res = await _client.PostAsJsonAsync("/categories",
            new CreateCategoryRequest("Banana", "X", TestData.AisleIceCream));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_unknown_parent_is_404()
    {
        var res = await _client.PostAsJsonAsync("/categories",
            new CreateCategoryRequest("Shelf", "Orphan", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_duplicate_path_is_409()
    {
        // "Beef" already exists under the Meat department at that path.
        var res = await _client.PostAsJsonAsync("/categories",
            new CreateCategoryRequest("Aisle", "Beef", TestData.DeptMeat));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Rename_changes_the_display_name_but_not_the_path()
    {
        var res = await _client.PatchAsJsonAsync($"/categories/{TestData.RenameMeShelf}",
            new RenameCategoryRequest("Renamed Shelf"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var updated = await res.Content.ReadFromJsonAsync<CategorySummary>();
        Assert.Equal("Renamed Shelf", updated!.Name);
        Assert.Equal("frozen/ice-cream-desserts/rename-me", updated.Path); // stable key unchanged

        var node = Find(await TreeAsync(), TestData.RenameMeShelf);
        Assert.Equal("Renamed Shelf", node!.Name);
    }

    [Fact]
    public async Task Rename_unknown_is_404()
    {
        var res = await _client.PatchAsJsonAsync($"/categories/{Guid.NewGuid()}",
            new RenameCategoryRequest("Nope"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Archive_hides_the_node_and_its_products_from_browse()
    {
        // Before: the shelf and its product are browsable.
        var before = await _client.GetFromJsonAsync<List<ProductGroup>>(
            $"/categories/{TestData.ArchiveMeShelf}/products");
        Assert.Contains(before!, g => g.ItemId == TestData.FrozenProduct);

        var res = await _client.DeleteAsync($"/categories/{TestData.ArchiveMeShelf}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // After: gone from the tree, and the node no longer resolves.
        Assert.Null(Find(await TreeAsync(), TestData.ArchiveMeShelf));
        var after = await _client.GetAsync($"/categories/{TestData.ArchiveMeShelf}/products");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Archive_unknown_is_404()
    {
        var res = await _client.DeleteAsync($"/categories/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

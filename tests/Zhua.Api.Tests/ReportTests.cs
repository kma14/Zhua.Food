using System.Net.Http.Json;

namespace Zhua.Api.Tests;

/// <summary>GET /reports/product-status — the per-chain match-status distribution (D30.1). Runs against real
/// Postgres, so it also proves the MatchKey-scheme GROUP BY translates.</summary>
[Collection(ApiCollection.Name)]
public class ReportTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Product_status_report_is_a_table_whose_columns_sum_to_each_total()
    {
        var report = await _client.GetFromJsonAsync<ProductStatusReport>("/reports/product-status");

        Assert.NotNull(report);

        // One row per active supermarket, in a stable order.
        Assert.Equal(
            new[] { "NewWorld", "PaknSave", "Woolworths", "FreshChoice" },
            report!.Chains.Select(c => c.Supermarket).ToArray());

        // Every listing lands in exactly one status → a row's columns sum to its Total.
        foreach (var row in report.Chains)
            Assert.Equal(
                row.FoodstuffsItem + row.WoolworthsItem + row.FreshChoiceItem
                    + row.ManualItem + row.PendingReview + row.Held,
                row.Total);

        // The Total row is the column-wise sum of the chain rows.
        Assert.Equal("Total", report.Total.Supermarket);
        Assert.Equal(report.Chains.Sum(c => c.Total), report.Total.Total);
        Assert.Equal(report.Chains.Sum(c => c.FoodstuffsItem), report.Total.FoodstuffsItem);
        Assert.True(report.Total.Total >= 1); // the seed has products
    }
}

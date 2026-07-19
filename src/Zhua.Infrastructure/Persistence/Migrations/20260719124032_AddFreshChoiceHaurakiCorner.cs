using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds the first FreshChoice store (plan D26): Hauraki Corner, 367 Lake Road, Hauraki (North Shore).
    /// ExternalStoreId = the MyFoodLink storefront subdomain ("hc" → hc.store.freshchoice.co.nz) — the crawler
    /// builds its base URL from it (each FreshChoice store is its own independently-priced storefront).
    /// </summary>
    public partial class AddFreshChoiceHaurakiCorner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "Chain", "ExternalStoreId", "IsActive", "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[]
                {
                    new Guid("44444444-4444-4444-4444-444444444444"), "FreshChoice", "hc", true,
                    -36.7943, 174.7691, "FreshChoice Hauraki Corner", "Hauraki",
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));
        }
    }
}

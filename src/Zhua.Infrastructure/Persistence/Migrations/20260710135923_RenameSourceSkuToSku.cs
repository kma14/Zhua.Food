using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameSourceSkuToSku : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SourceSku",
                table: "Products",
                newName: "Sku");

            migrationBuilder.RenameIndex(
                name: "IX_Products_StoreId_SourceSku",
                table: "Products",
                newName: "IX_Products_StoreId_Sku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Sku",
                table: "Products",
                newName: "SourceSku");

            migrationBuilder.RenameIndex(
                name: "IX_Products_StoreId_Sku",
                table: "Products",
                newName: "IX_Products_StoreId_SourceSku");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoTypeModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MemberPrice",
                table: "Products",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MultibuyQuantity",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MultibuyTotal",
                table: "Products",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromoType",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MemberPrice",
                table: "PriceSnapshots",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MultibuyQuantity",
                table: "PriceSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MultibuyTotal",
                table: "PriceSnapshots",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromoType",
                table: "PriceSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: rows crawled before this model treated every promo as a public special, so keep them
            // self-consistent (IsOnSpecial=true ⇒ PromoType=Special) until the next crawl re-types them properly.
            migrationBuilder.Sql("""UPDATE "Products" SET "PromoType" = 1 WHERE "IsOnSpecial";""");
            migrationBuilder.Sql("""UPDATE "PriceSnapshots" SET "PromoType" = 1 WHERE "IsOnSpecial";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemberPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MultibuyQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MultibuyTotal",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PromoType",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MemberPrice",
                table: "PriceSnapshots");

            migrationBuilder.DropColumn(
                name: "MultibuyQuantity",
                table: "PriceSnapshots");

            migrationBuilder.DropColumn(
                name: "MultibuyTotal",
                table: "PriceSnapshots");

            migrationBuilder.DropColumn(
                name: "PromoType",
                table: "PriceSnapshots");
        }
    }
}

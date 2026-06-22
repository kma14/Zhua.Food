using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoodstuffsStoresAndWiderCategoryId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                table: "StoreCategories",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "ExternalStoreId",
                value: "60928d93-06fa-4d8f-92a6-8c359e7e846d");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[] { -36.722799999999999, 174.70050000000001, "PAK'nSAVE Albany", "Albany" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                table: "StoreCategories",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "ExternalStoreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[] { -36.778300000000002, 174.74469999999999, "PAK'nSAVE Glenfield", "Glenfield" });
        }
    }
}

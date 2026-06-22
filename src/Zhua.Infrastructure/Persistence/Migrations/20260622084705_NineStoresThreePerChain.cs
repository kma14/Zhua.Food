using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NineStoresThreePerChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[] { -36.846400000000003, 174.76589999999999, "New World Metro Auckland", "Auckland Central" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "ExternalStoreId", "Latitude", "Longitude" },
                values: new object[] { "65defcf2-bc15-490e-a84f-1f13b769cd22", -36.729999999999997, 174.70670000000001 });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "Chain", "ExternalStoreId", "IsActive", "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111112"), "Woolworths", null, true, -36.780700000000003, 174.72280000000001, "Woolworths Glenfield", "Glenfield" },
                    { new Guid("11111111-1111-1111-1111-111111111113"), "Woolworths", null, true, -36.7166, 174.7466, "Woolworths Browns Bay", "Browns Bay" },
                    { new Guid("22222222-2222-2222-2222-222222222223"), "NewWorld", "1898a189-acf3-4320-8704-7a9cc6b3924d", true, -36.787599999999998, 174.77000000000001, "New World Shore City", "Takapuna" },
                    { new Guid("22222222-2222-2222-2222-222222222224"), "NewWorld", "dbdfdd2a-55f7-4870-9b51-979286323647", true, -36.716000000000001, 174.7473, "New World Browns Bay", "Browns Bay" },
                    { new Guid("33333333-3333-3333-3333-333333333334"), "PaknSave", "60561e46-ece7-43a7-b142-9b14812586e4", true, -36.930700000000002, 174.91300000000001, "PAK'nSAVE Botany", "Botany" },
                    { new Guid("33333333-3333-3333-3333-333333333335"), "PaknSave", "2a1b331a-fc4a-496a-b072-e97cc8f70cae", true, -36.898899999999998, 174.90469999999999, "PAK'nSAVE Highland Park", "Highland Park" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111112"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111113"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222223"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222224"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333334"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333335"));

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[] { -36.786799999999999, 174.7731, "New World Takapuna", "Takapuna" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "ExternalStoreId", "Latitude", "Longitude" },
                values: new object[] { null, -36.722799999999999, 174.70050000000001 });
        }
    }
}

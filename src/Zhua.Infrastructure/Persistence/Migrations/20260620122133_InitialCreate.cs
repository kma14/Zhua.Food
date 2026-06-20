using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanonicalProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Brand = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Gtin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Chain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Suburb = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ExternalStoreId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawlRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProductsFound = table.Column<int>(type: "integer", nullable: false),
                    SnapshotsWritten = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrawlRuns_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RawBrand = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    RawSize = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Gtin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CurrentPrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    CurrentSpecial = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsOnSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProducts_CanonicalProducts_CanonicalProductId",
                        column: x => x.CanonicalProductId,
                        principalTable: "CanonicalProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StoreProducts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrawlRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    NonSpecialPrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsOnSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceSnapshots_CrawlRuns_CrawlRunId",
                        column: x => x.CrawlRunId,
                        principalTable: "CrawlRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceSnapshots_StoreProducts_StoreProductId",
                        column: x => x.StoreProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "Chain", "ExternalStoreId", "IsActive", "Latitude", "Longitude", "Name", "Suburb" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "Woolworths", null, true, -36.7879, 174.76949999999999, "Woolworths Takapuna", "Takapuna" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "NewWorld", null, true, -36.786799999999999, 174.7731, "New World Takapuna", "Takapuna" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "PaknSave", null, true, -36.778300000000002, 174.74469999999999, "PAK'nSAVE Glenfield", "Glenfield" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalProducts_Category",
                table: "CanonicalProducts",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalProducts_Gtin",
                table: "CanonicalProducts",
                column: "Gtin");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlRuns_StoreId_StartedAt",
                table: "CrawlRuns",
                columns: new[] { "StoreId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshots_CrawlRunId",
                table: "PriceSnapshots",
                column: "CrawlRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshots_StoreProductId_CapturedAt",
                table: "PriceSnapshots",
                columns: new[] { "StoreProductId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_CanonicalProductId",
                table: "StoreProducts",
                column: "CanonicalProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_Gtin",
                table: "StoreProducts",
                column: "Gtin");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_StoreId_SourceSku",
                table: "StoreProducts",
                columns: new[] { "StoreId", "SourceSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Chain",
                table: "Stores",
                column: "Chain");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceSnapshots");

            migrationBuilder.DropTable(
                name: "CrawlRuns");

            migrationBuilder.DropTable(
                name: "StoreProducts");

            migrationBuilder.DropTable(
                name: "CanonicalProducts");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}

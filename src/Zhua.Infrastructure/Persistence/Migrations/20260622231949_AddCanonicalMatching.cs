using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchKey",
                table: "CanonicalProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatchCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchCandidates_CanonicalProducts_CanonicalProductId",
                        column: x => x.CanonicalProductId,
                        principalTable: "CanonicalProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchCandidates_StoreProducts_StoreProductId",
                        column: x => x.StoreProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalProducts_MatchKey",
                table: "CanonicalProducts",
                column: "MatchKey",
                unique: true,
                filter: "\"MatchKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCandidates_CanonicalProductId",
                table: "MatchCandidates",
                column: "CanonicalProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCandidates_Status",
                table: "MatchCandidates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCandidates_StoreProductId_CanonicalProductId",
                table: "MatchCandidates",
                columns: new[] { "StoreProductId", "CanonicalProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchCandidates");

            migrationBuilder.DropIndex(
                name: "IX_CanonicalProducts_MatchKey",
                table: "CanonicalProducts");

            migrationBuilder.DropColumn(
                name: "MatchKey",
                table: "CanonicalProducts");
        }
    }
}

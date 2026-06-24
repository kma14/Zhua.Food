using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CanonicalCategoryId",
                table: "StoreCategories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CanonicalCategoryId",
                table: "CanonicalProducts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CanonicalCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalCategories_CanonicalCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CanonicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_CanonicalCategoryId",
                table: "StoreCategories",
                column: "CanonicalCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalProducts_CanonicalCategoryId",
                table: "CanonicalProducts",
                column: "CanonicalCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCategories_ParentId",
                table: "CanonicalCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalCategories_Path",
                table: "CanonicalCategories",
                column: "Path",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CanonicalProducts_CanonicalCategories_CanonicalCategoryId",
                table: "CanonicalProducts",
                column: "CanonicalCategoryId",
                principalTable: "CanonicalCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StoreCategories_CanonicalCategories_CanonicalCategoryId",
                table: "StoreCategories",
                column: "CanonicalCategoryId",
                principalTable: "CanonicalCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CanonicalProducts_CanonicalCategories_CanonicalCategoryId",
                table: "CanonicalProducts");

            migrationBuilder.DropForeignKey(
                name: "FK_StoreCategories_CanonicalCategories_CanonicalCategoryId",
                table: "StoreCategories");

            migrationBuilder.DropTable(
                name: "CanonicalCategories");

            migrationBuilder.DropIndex(
                name: "IX_StoreCategories_CanonicalCategoryId",
                table: "StoreCategories");

            migrationBuilder.DropIndex(
                name: "IX_CanonicalProducts_CanonicalCategoryId",
                table: "CanonicalProducts");

            migrationBuilder.DropColumn(
                name: "CanonicalCategoryId",
                table: "StoreCategories");

            migrationBuilder.DropColumn(
                name: "CanonicalCategoryId",
                table: "CanonicalProducts");
        }
    }
}

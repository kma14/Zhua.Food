using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemMergeRedirect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_MergedIntoId",
                table: "Items",
                column: "MergedIntoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Items_MergedIntoId",
                table: "Items",
                column: "MergedIntoId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Items_MergedIntoId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_MergedIntoId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "Items");
        }
    }
}

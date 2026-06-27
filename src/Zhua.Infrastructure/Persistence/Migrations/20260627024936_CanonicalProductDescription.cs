using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalProductDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CanonicalProducts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            // One-time backfill: seed the new owned phrase from the existing (source-derived) Name (plan D25).
            // From here on the matcher sets Description only on creation and never re-mints it.
            migrationBuilder.Sql("UPDATE \"CanonicalProducts\" SET \"Description\" = \"Name\" WHERE \"Description\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "CanonicalProducts");
        }
    }
}

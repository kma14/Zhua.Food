using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only: drops every Woolworths <c>StoreCategory</c> so the next complete crawl rebuilds the tree under the
    /// new identity. <c>ExternalId</c> for Woolworths changed from the response's numeric breadcrumb id to the slug
    /// path we request — Woolworths <b>recycles</b> those numeric ids, and a recycled id lands on an existing node of
    /// a different category, which silently re-filed 3,424 products and mislabelled 633 items (milk shown under
    /// "Carrots &amp; Root Vegetables", 2026-07-26). Old rows can't be rewritten in place: deriving their true path
    /// needs the parent chain, and that chain is itself polluted by the same churn (733 rows for 507 real nodes).
    ///
    /// Safe to delete because all three are re-derived every cycle: product↔category links are reset by each complete
    /// crawl (D30.2), <c>StoreCategory.CategoryId</c> is recomputed by <c>CategoryMapper</c> every match run, and the
    /// shared <c>Category</c> tree is seeded from the Foodstuffs taxonomy (untouched here). Until the next Woolworths
    /// crawl its products are absent from category browse — run one right after applying this.
    /// </summary>
    public partial class WoolworthsCategoryIdentityToSlugPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Product links first (the join table has no cascade from this direction we can rely on ordering-wise).
            migrationBuilder.Sql("""
                DELETE FROM "ProductStoreCategory" psc
                USING "StoreCategories" sc, "Stores" s
                WHERE psc."CategoriesId" = sc."Id" AND sc."StoreId" = s."Id" AND s."Chain" = 'Woolworths';
                """);

            // Leaves before parents: StoreCategory.ParentId is ON DELETE RESTRICT, so a single unordered DELETE
            // would trip the self-referencing FK.
            foreach (var kind in new[] { "Shelf", "Aisle", "Department" })
            {
                migrationBuilder.Sql($"""
                    DELETE FROM "StoreCategories" sc
                    USING "Stores" s
                    WHERE sc."StoreId" = s."Id" AND s."Chain" = 'Woolworths' AND sc."Kind" = '{kind}';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the deleted rows are crawl-derived. Re-crawl Woolworths to rebuild them.
        }
    }
}

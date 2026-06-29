using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zhua.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the canonical-layer schema to the new vocabulary (D25 rename): CanonicalProduct→Item,
    /// StoreProduct→Product, CanonicalCategory→Category. Pure <c>ALTER … RENAME</c> — data-preserving (no drop/create);
    /// renames tables, FK columns, indexes, and PK/FK constraints so the physical names stay in sync with the model
    /// and future migrations don't drift. Hand-written (EF scaffolds a destructive drop/create for renames).
    /// </summary>
    public partial class RenameCanonicalToItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            -- 1) Tables
            ALTER TABLE "CanonicalProducts"         RENAME TO "Items";
            ALTER TABLE "StoreProducts"             RENAME TO "Products";
            ALTER TABLE "CanonicalCategories"       RENAME TO "Categories";
            ALTER TABLE "ProductTagStoreProduct"    RENAME TO "ProductProductTag";
            ALTER TABLE "StoreCategoryStoreProduct" RENAME TO "ProductStoreCategory";

            -- 2) FK columns
            ALTER TABLE "Items"           RENAME COLUMN "CanonicalCategoryId" TO "CategoryId";
            ALTER TABLE "Products"        RENAME COLUMN "CanonicalProductId"  TO "ItemId";
            ALTER TABLE "StoreCategories" RENAME COLUMN "CanonicalCategoryId" TO "CategoryId";
            ALTER TABLE "MatchCandidates" RENAME COLUMN "CanonicalProductId"  TO "ItemId";
            ALTER TABLE "MatchCandidates" RENAME COLUMN "StoreProductId"      TO "ProductId";
            ALTER TABLE "PriceSnapshots"  RENAME COLUMN "StoreProductId"      TO "ProductId";

            -- 3) Primary keys (PG renames the backing index with the constraint)
            ALTER TABLE "Items"                RENAME CONSTRAINT "PK_CanonicalProducts"         TO "PK_Items";
            ALTER TABLE "Products"             RENAME CONSTRAINT "PK_StoreProducts"             TO "PK_Products";
            ALTER TABLE "Categories"           RENAME CONSTRAINT "PK_CanonicalCategories"       TO "PK_Categories";
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "PK_ProductTagStoreProduct"    TO "PK_ProductProductTag";
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "PK_StoreCategoryStoreProduct" TO "PK_ProductStoreCategory";

            -- 4) Indexes
            ALTER INDEX "IX_CanonicalCategories_ParentId"                     RENAME TO "IX_Categories_ParentId";
            ALTER INDEX "IX_CanonicalCategories_Path"                         RENAME TO "IX_Categories_Path";
            ALTER INDEX "IX_CanonicalProducts_CanonicalCategoryId"            RENAME TO "IX_Items_CategoryId";
            ALTER INDEX "IX_CanonicalProducts_Category"                       RENAME TO "IX_Items_Category";
            ALTER INDEX "IX_CanonicalProducts_Gtin"                          RENAME TO "IX_Items_Gtin";
            ALTER INDEX "IX_CanonicalProducts_MatchKey"                       RENAME TO "IX_Items_MatchKey";
            ALTER INDEX "IX_StoreProducts_CanonicalProductId"                 RENAME TO "IX_Products_ItemId";
            ALTER INDEX "IX_StoreProducts_Gtin"                              RENAME TO "IX_Products_Gtin";
            ALTER INDEX "IX_StoreProducts_StoreId_SourceSku"                  RENAME TO "IX_Products_StoreId_SourceSku";
            ALTER INDEX "IX_StoreCategories_CanonicalCategoryId"              RENAME TO "IX_StoreCategories_CategoryId";
            ALTER INDEX "IX_MatchCandidates_CanonicalProductId"              RENAME TO "IX_MatchCandidates_ItemId";
            ALTER INDEX "IX_MatchCandidates_StoreProductId_CanonicalProductId" RENAME TO "IX_MatchCandidates_ProductId_ItemId";
            ALTER INDEX "IX_PriceSnapshots_StoreProductId_CapturedAt"         RENAME TO "IX_PriceSnapshots_ProductId_CapturedAt";
            ALTER INDEX "IX_ProductTagStoreProduct_TagsId"                    RENAME TO "IX_ProductProductTag_TagsId";
            ALTER INDEX "IX_StoreCategoryStoreProduct_ProductsId"             RENAME TO "IX_ProductStoreCategory_ProductsId";

            -- 5) Foreign keys
            ALTER TABLE "Items"                RENAME CONSTRAINT "FK_CanonicalProducts_CanonicalCategories_CanonicalCategoryId" TO "FK_Items_Categories_CategoryId";
            ALTER TABLE "Products"             RENAME CONSTRAINT "FK_StoreProducts_CanonicalProducts_CanonicalProductId"        TO "FK_Products_Items_ItemId";
            ALTER TABLE "Products"             RENAME CONSTRAINT "FK_StoreProducts_Stores_StoreId"                              TO "FK_Products_Stores_StoreId";
            ALTER TABLE "PriceSnapshots"       RENAME CONSTRAINT "FK_PriceSnapshots_StoreProducts_StoreProductId"              TO "FK_PriceSnapshots_Products_ProductId";
            ALTER TABLE "StoreCategories"      RENAME CONSTRAINT "FK_StoreCategories_CanonicalCategories_CanonicalCategoryId"   TO "FK_StoreCategories_Categories_CategoryId";
            ALTER TABLE "MatchCandidates"      RENAME CONSTRAINT "FK_MatchCandidates_CanonicalProducts_CanonicalProductId"      TO "FK_MatchCandidates_Items_ItemId";
            ALTER TABLE "MatchCandidates"      RENAME CONSTRAINT "FK_MatchCandidates_StoreProducts_StoreProductId"              TO "FK_MatchCandidates_Products_ProductId";
            ALTER TABLE "Categories"           RENAME CONSTRAINT "FK_CanonicalCategories_CanonicalCategories_ParentId"          TO "FK_Categories_Categories_ParentId";
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "FK_StoreCategoryStoreProduct_StoreCategories_CategoriesId"    TO "FK_ProductStoreCategory_StoreCategories_CategoriesId";
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "FK_StoreCategoryStoreProduct_StoreProducts_ProductsId"        TO "FK_ProductStoreCategory_Products_ProductsId";
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "FK_ProductTagStoreProduct_ProductTags_TagsId"                 TO "FK_ProductProductTag_ProductTags_TagsId";
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "FK_ProductTagStoreProduct_StoreProducts_ProductsId"           TO "FK_ProductProductTag_Products_ProductsId";
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            -- 5) Foreign keys
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "FK_ProductProductTag_Products_ProductsId"                     TO "FK_ProductTagStoreProduct_StoreProducts_ProductsId";
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "FK_ProductProductTag_ProductTags_TagsId"                      TO "FK_ProductTagStoreProduct_ProductTags_TagsId";
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "FK_ProductStoreCategory_Products_ProductsId"                  TO "FK_StoreCategoryStoreProduct_StoreProducts_ProductsId";
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "FK_ProductStoreCategory_StoreCategories_CategoriesId"         TO "FK_StoreCategoryStoreProduct_StoreCategories_CategoriesId";
            ALTER TABLE "Categories"           RENAME CONSTRAINT "FK_Categories_Categories_ParentId"                           TO "FK_CanonicalCategories_CanonicalCategories_ParentId";
            ALTER TABLE "MatchCandidates"      RENAME CONSTRAINT "FK_MatchCandidates_Products_ProductId"                       TO "FK_MatchCandidates_StoreProducts_StoreProductId";
            ALTER TABLE "MatchCandidates"      RENAME CONSTRAINT "FK_MatchCandidates_Items_ItemId"                            TO "FK_MatchCandidates_CanonicalProducts_CanonicalProductId";
            ALTER TABLE "StoreCategories"      RENAME CONSTRAINT "FK_StoreCategories_Categories_CategoryId"                     TO "FK_StoreCategories_CanonicalCategories_CanonicalCategoryId";
            ALTER TABLE "PriceSnapshots"       RENAME CONSTRAINT "FK_PriceSnapshots_Products_ProductId"                       TO "FK_PriceSnapshots_StoreProducts_StoreProductId";
            ALTER TABLE "Products"             RENAME CONSTRAINT "FK_Products_Stores_StoreId"                                  TO "FK_StoreProducts_Stores_StoreId";
            ALTER TABLE "Products"             RENAME CONSTRAINT "FK_Products_Items_ItemId"                                    TO "FK_StoreProducts_CanonicalProducts_CanonicalProductId";
            ALTER TABLE "Items"                RENAME CONSTRAINT "FK_Items_Categories_CategoryId"                              TO "FK_CanonicalProducts_CanonicalCategories_CanonicalCategoryId";

            -- 4) Indexes
            ALTER INDEX "IX_ProductStoreCategory_ProductsId"             RENAME TO "IX_StoreCategoryStoreProduct_ProductsId";
            ALTER INDEX "IX_ProductProductTag_TagsId"                    RENAME TO "IX_ProductTagStoreProduct_TagsId";
            ALTER INDEX "IX_PriceSnapshots_ProductId_CapturedAt"         RENAME TO "IX_PriceSnapshots_StoreProductId_CapturedAt";
            ALTER INDEX "IX_MatchCandidates_ProductId_ItemId"            RENAME TO "IX_MatchCandidates_StoreProductId_CanonicalProductId";
            ALTER INDEX "IX_MatchCandidates_ItemId"                      RENAME TO "IX_MatchCandidates_CanonicalProductId";
            ALTER INDEX "IX_StoreCategories_CategoryId"                  RENAME TO "IX_StoreCategories_CanonicalCategoryId";
            ALTER INDEX "IX_Products_StoreId_SourceSku"                  RENAME TO "IX_StoreProducts_StoreId_SourceSku";
            ALTER INDEX "IX_Products_Gtin"                              RENAME TO "IX_StoreProducts_Gtin";
            ALTER INDEX "IX_Products_ItemId"                            RENAME TO "IX_StoreProducts_CanonicalProductId";
            ALTER INDEX "IX_Items_MatchKey"                             RENAME TO "IX_CanonicalProducts_MatchKey";
            ALTER INDEX "IX_Items_Gtin"                                 RENAME TO "IX_CanonicalProducts_Gtin";
            ALTER INDEX "IX_Items_Category"                             RENAME TO "IX_CanonicalProducts_Category";
            ALTER INDEX "IX_Items_CategoryId"                           RENAME TO "IX_CanonicalProducts_CanonicalCategoryId";
            ALTER INDEX "IX_Categories_Path"                            RENAME TO "IX_CanonicalCategories_Path";
            ALTER INDEX "IX_Categories_ParentId"                        RENAME TO "IX_CanonicalCategories_ParentId";

            -- 3) Primary keys
            ALTER TABLE "ProductStoreCategory" RENAME CONSTRAINT "PK_ProductStoreCategory" TO "PK_StoreCategoryStoreProduct";
            ALTER TABLE "ProductProductTag"    RENAME CONSTRAINT "PK_ProductProductTag"    TO "PK_ProductTagStoreProduct";
            ALTER TABLE "Categories"           RENAME CONSTRAINT "PK_Categories"           TO "PK_CanonicalCategories";
            ALTER TABLE "Products"             RENAME CONSTRAINT "PK_Products"             TO "PK_StoreProducts";
            ALTER TABLE "Items"                RENAME CONSTRAINT "PK_Items"                TO "PK_CanonicalProducts";

            -- 2) FK columns
            ALTER TABLE "PriceSnapshots"  RENAME COLUMN "ProductId"  TO "StoreProductId";
            ALTER TABLE "MatchCandidates" RENAME COLUMN "ProductId"  TO "StoreProductId";
            ALTER TABLE "MatchCandidates" RENAME COLUMN "ItemId"     TO "CanonicalProductId";
            ALTER TABLE "StoreCategories" RENAME COLUMN "CategoryId" TO "CanonicalCategoryId";
            ALTER TABLE "Products"        RENAME COLUMN "ItemId"     TO "CanonicalProductId";
            ALTER TABLE "Items"           RENAME COLUMN "CategoryId" TO "CanonicalCategoryId";

            -- 1) Tables
            ALTER TABLE "ProductStoreCategory" RENAME TO "StoreCategoryStoreProduct";
            ALTER TABLE "ProductProductTag"    RENAME TO "ProductTagStoreProduct";
            ALTER TABLE "Categories"           RENAME TO "CanonicalCategories";
            ALTER TABLE "Products"             RENAME TO "StoreProducts";
            ALTER TABLE "Items"                RENAME TO "CanonicalProducts";
            """);
    }
}

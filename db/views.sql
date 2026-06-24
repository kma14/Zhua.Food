-- =====================================================================================
-- zhua.food — ops convenience VIEWS (read-only helpers over the ingested data).
--
-- These are NOT part of the EF migration pipeline (the schema is owned by migrations, D5).
-- They are idempotent (CREATE OR REPLACE) and re-appliable any time:
--
--   docker exec -i zhuafood-postgres-1 psql -U zhua -d zhua < db/views.sql
--
-- Timestamps are rendered in Pacific/Auckland for readability. Prices are NZD.
-- Quick ad-hoc queries (parameterised) are at the bottom as commented examples.
-- =====================================================================================

-- 1) SAME-PRODUCT CROSS-STORE COMPARE — the core view (D9). One row per (canonical × store),
--    each store's own name + price + unit price. Filter by a canonical_id or product name.
CREATE OR REPLACE VIEW v_product_compare AS
SELECT c."Id"                                      AS canonical_id,
       c."Name"                                    AS product,
       c."Brand"                                   AS brand,
       c."Size"                                    AS size,
       c."Category"                                AS category,
       s."Chain"                                   AS chain,
       s."Name"                                    AS store,
       s."Suburb"                                  AS suburb,
       sp."RawName"                                AS store_name,
       sp."CurrentPrice"                           AS price,
       sp."IsOnSpecial"                            AS on_special,
       sp."CurrentNonSpecialPrice"                 AS was_price,
       sp."UnitPrice"                              AS unit_price,
       sp."UnitOfMeasure"                          AS uom,
       sp."LastSeenAt" AT TIME ZONE 'Pacific/Auckland' AS last_seen
FROM "CanonicalProducts" c
JOIN "StoreProducts" sp ON sp."CanonicalProductId" = c."Id"
JOIN "Stores" s         ON s."Id" = sp."StoreId"
ORDER BY c."Name", sp."CurrentPrice";

-- 2) CHEAPEST PER PRODUCT — one row per canonical: cheapest store + price, how many stores
--    carry it, and the spread to the dearest. Answers "where is X cheapest" at a glance.
CREATE OR REPLACE VIEW v_cheapest_by_product AS
SELECT DISTINCT ON (c."Id")
       c."Id"            AS canonical_id,
       c."Name"          AS product,
       c."Brand"         AS brand,
       c."Size"          AS size,
       c."Category"      AS category,
       sp."CurrentPrice" AS cheapest_price,
       s."Name"          AS cheapest_store,
       s."Chain"         AS chain,
       agg.store_count,
       agg.max_price,
       round(agg.max_price - sp."CurrentPrice", 2) AS spread
FROM "CanonicalProducts" c
JOIN "StoreProducts" sp ON sp."CanonicalProductId" = c."Id"
JOIN "Stores" s         ON s."Id" = sp."StoreId"
JOIN LATERAL (
    SELECT count(*) AS store_count, max(x."CurrentPrice") AS max_price
    FROM "StoreProducts" x
    WHERE x."CanonicalProductId" = c."Id"
) agg ON true
ORDER BY c."Id", sp."CurrentPrice" ASC;   -- DISTINCT ON keeps the cheapest row per canonical

-- 3) CURRENT SPECIALS (deals) — everything on special now, biggest saving first.
CREATE OR REPLACE VIEW v_current_specials AS
SELECT s."Chain"                  AS chain,
       s."Name"                   AS store,
       sp."RawName"               AS product,
       sp."RawBrand"              AS brand,
       sp."CurrentPrice"          AS price,
       sp."CurrentNonSpecialPrice" AS was_price,
       round(sp."CurrentNonSpecialPrice" - sp."CurrentPrice", 2) AS saving,
       round(100.0 * (sp."CurrentNonSpecialPrice" - sp."CurrentPrice")
             / nullif(sp."CurrentNonSpecialPrice", 0), 0)        AS pct_off,
       sp."UnitPrice"             AS unit_price,
       sp."UnitOfMeasure"         AS uom,
       sp."CanonicalProductId"    AS canonical_id
FROM "StoreProducts" sp
JOIN "Stores" s ON s."Id" = sp."StoreId"
WHERE sp."IsOnSpecial" = true
  AND sp."CurrentNonSpecialPrice" IS NOT NULL
ORDER BY saving DESC NULLS LAST;

-- 4) PRICE HISTORY — the change-only snapshots over time (D3), per store-product.
--    This is the API's biggest gap; the data is all here. Filter by product / canonical_id.
CREATE OR REPLACE VIEW v_price_history AS
SELECT sp."CanonicalProductId"    AS canonical_id,
       s."Chain"                  AS chain,
       s."Name"                   AS store,
       sp."RawName"               AS product,
       ps."CapturedAt" AT TIME ZONE 'Pacific/Auckland' AS captured_at,
       ps."Price"                 AS price,
       ps."NonSpecialPrice"       AS was_price,
       ps."IsOnSpecial"           AS on_special,
       ps."UnitPrice"             AS unit_price
FROM "PriceSnapshots" ps
JOIN "StoreProducts" sp ON sp."Id" = ps."StoreProductId"
JOIN "Stores" s         ON s."Id" = sp."StoreId"
ORDER BY sp."RawName", s."Name", ps."CapturedAt";

-- 5) CRAWL RUNS — observability. Newest first, with duration and per-run counts.
CREATE OR REPLACE VIEW v_crawl_runs AS
SELECT cr."StartedAt"  AT TIME ZONE 'Pacific/Auckland' AS started,
       cr."FinishedAt" AT TIME ZONE 'Pacific/Auckland' AS finished,
       round(extract(epoch FROM (cr."FinishedAt" - cr."StartedAt"))::numeric, 0) AS seconds,
       s."Name"             AS store,
       s."Chain"            AS chain,
       cr."Status"          AS status,
       cr."ProductsFound"   AS products,
       cr."SnapshotsWritten" AS snapshots,
       cr."ErrorMessage"    AS error
FROM "CrawlRuns" cr
JOIN "Stores" s ON s."Id" = cr."StoreId"
ORDER BY cr."StartedAt" DESC;

-- 6) BRANCH PRICE VARIATION — same canonical priced differently across branches of one chain
--    (the franchise-pricing insight, D16). Biggest spread first.
CREATE OR REPLACE VIEW v_branch_price_variation AS
SELECT c."Id"     AS canonical_id,
       c."Name"   AS product,
       s."Chain"  AS chain,
       count(*)   AS branches,
       min(sp."CurrentPrice") AS min_price,
       max(sp."CurrentPrice") AS max_price,
       round(max(sp."CurrentPrice") - min(sp."CurrentPrice"), 2) AS spread
FROM "CanonicalProducts" c
JOIN "StoreProducts" sp ON sp."CanonicalProductId" = c."Id"
JOIN "Stores" s         ON s."Id" = sp."StoreId"
GROUP BY c."Id", c."Name", s."Chain"
HAVING count(*) > 1 AND max(sp."CurrentPrice") <> min(sp."CurrentPrice")
ORDER BY spread DESC;

-- 7) MATCH REVIEW QUEUE — pending canonical-match candidates for human review (D18).
CREATE OR REPLACE VIEW v_match_review AS
SELECT mc."Id"          AS candidate_id,
       s."Chain"        AS chain,
       s."Name"         AS store,
       sp."RawName"     AS store_product,
       sp."RawBrand"    AS brand,
       sp."RawSize"     AS size,
       sp."CurrentPrice" AS price,
       c."Name"         AS candidate_canonical,
       mc."Score"       AS score,
       mc."Reason"      AS reason
FROM "MatchCandidates" mc
JOIN "StoreProducts" sp     ON sp."Id" = mc."StoreProductId"
JOIN "Stores" s             ON s."Id" = sp."StoreId"
JOIN "CanonicalProducts" c  ON c."Id" = mc."CanonicalProductId"
WHERE mc."Status" = 'Pending'
ORDER BY mc."Score" DESC;

-- 8) CANONICAL CATEGORY TREE (D22) — the shared cross-store taxonomy with product counts per node.
CREATE OR REPLACE VIEW v_canonical_category AS
SELECT cc."Kind"  AS kind,
       cc."Path"  AS path,
       cc."Name"  AS name,
       count(cp.*) AS products
FROM "CanonicalCategories" cc
LEFT JOIN "CanonicalProducts" cp ON cp."CanonicalCategoryId" = cc."Id"
GROUP BY cc."Id"
ORDER BY cc."Path";

-- =====================================================================================
-- Ad-hoc query recipes (copy + fill in the blank). Not views — just handy patterns.
-- =====================================================================================
--
-- Find a product, then compare it across stores:
--   SELECT * FROM v_cheapest_by_product WHERE product ILIKE '%colby%';
--   SELECT chain, store, store_name, price, on_special, unit_price, uom
--     FROM v_product_compare WHERE canonical_id = '<paste-id>';
--
-- Price history of one product over time:
--   SELECT captured_at, store, price, was_price, on_special
--     FROM v_price_history WHERE product ILIKE '%beef eye fillet%' ORDER BY captured_at;
--
-- Today's deals for one chain:
--   SELECT * FROM v_current_specials WHERE chain = 'PaknSave' LIMIT 30;
--
-- Did the scheduled crawl run, and how long did each store take:
--   SELECT * FROM v_crawl_runs LIMIT 14;
--
-- Where does branch pricing matter most (franchise effect):
--   SELECT * FROM v_branch_price_variation WHERE chain = 'PaknSave' LIMIT 20;

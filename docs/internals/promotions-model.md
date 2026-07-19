# zhua.food — promotions / specials model

How we model the **different kinds of price promotion** — instead of the old single binary "on special" flag — and
where promo start/end windows come from (our own observation history; no source publishes dates). Decided
2026-07-17 (A–E + A′, Decision log below) and **built the same day**: `PromoType` enum + `MemberPrice`/multibuy
pair on `Product` + `PriceSnapshot` (migration `20260717095635_AddPromoTypeModel`), both crawlers remapped,
`/deals` narrowed to public specials, API exposes `promoType`/`memberPrice`/`multibuy*` ([../api.md](../api.md)).

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-12 11:37** — 🧑‍⚖️ *(Kevin)* Model the **distinct types** of promotion (temporary special vs member/club
  price vs everyday-low vs multibuy, etc.) rather than lumping them all under one `IsOnSpecial`, and capture each
  special's **start/end time** where available. Prompted by noticing Woolworths `/deals` seems to mix all specials,
  separated only by whether a was-price exists. Record the design here; continue later.
- **2026-07-17 15:27** — 🧑‍⚖️ *(Kevin)* **Decision A:** start with two buckets — `Special` + `MemberPrice`
  (Multibuy/EverydayLow deferred until each chain's vocabulary is recon'd).
- **2026-07-17 15:27** — 🧑‍⚖️ *(Kevin)* **Decision C:** `/deals` carries **temporary `Special` only**; the member
  price is a **separate axis** shown beside the shelf price (e.g. "member $X"), never a "deal".
- **2026-07-17 15:27** — 🧑‍⚖️ *(Kevin)* **Decision E (direction):** knowing when a special starts/ends **is
  required** — rejected the "skip dates" recommendation; sources don't publish dates, so derive **observed**
  windows from our own crawl history (details below). B and D still pending (Kevin asked for fuller explanations).
- **2026-07-17 15:40** — 🧑‍⚖️ *(Kevin)* **Decision B = B2:** `PromoType` lives on `Product` (current state) **and
  joins the D3 change-only snapshot tuple** → `{Price, IsOnSpecial, NonSpecialPrice, UnitPrice, PromoType}`, so a
  type change (e.g. member→special at the same price) is recorded in history. No separate `Promotion` entity —
  it's an enum column, not an aggregate (the entity shape was the rejected D2 option).
- **2026-07-17 15:40** — 🧑‍⚖️ *(Kevin)* **Decision D = D1:** take the **primary promo only** (Foodstuffs
  `bestPromotion`) — "一般也只对最高的折扣的促销感兴趣". Raw badges still all flow into the D13 `ProductTag`
  dimension + D12 raw archive, so nothing is silently lost.
- **2026-07-17 15:40** — 🧑‍⚖️ *(Kevin)* **Decision E (step 1): recon for dates first** — inspect full live/raw
  responses (WW + Foodstuffs) for promo start/end fields before finalising the date design; observed-window
  derivation from snapshots (E1) is the fallback either way.
- **2026-07-17 16:05** — Full-archive recon done (2,725 FS promos + 14,684 WW products, D12 raw archive): **no
  source publishes promo dates** (WW `promotionStartDate/EndDate` exist but always null in the browse API) → dates
  = observed windows from our own snapshot history. Decoded: FS `cardDependencyFlag` = member signal (93% of NW
  promos are Clubcard-only!), `threshold>1` = multibuy ("3 for $5", `rewardValue` = total), decal ids
  3000/4000/5000/6000; WW `isClubPrice` ⊂ `isSpecial`, `productTag.multiBuy {quantity,value}`. See the per-chain
  section. Open: A′ (add `Multibuy` enum value now that it's decoded) + the fate of `IsOnSpecial`.
- **2026-07-17 18:58** — 🧑‍⚖️ *(Kevin)* **A′ approved: add `Multibuy`** to the enum now → `PromoType = None ·
  Special · MemberPrice · Multibuy`. `/deals` still surfaces `Special` only (C). Also: add a per-chain promo-system
  cheat sheet to this doc — the scattered findings were getting hard to hold in one head.
- **2026-07-17 23:41** — 🧑‍⚖️ *(Kevin: "ok做吧")* **Built.** `PromoType` + `MemberPrice` + `MultibuyQuantity/Total`
  on `Product` and `PriceSnapshot`; D3 tuple now `{Price, PromoType, NonSpecialPrice, UnitPrice, MemberPrice,
  Multibuy pair}`; **`IsOnSpecial` kept as a stored column but narrowed to `PromoType == Special`** (derived in
  `ApplyObservation`; keeps the `/deals` predicate and D23 guard untouched — the open "fate of IsOnSpecial"
  question is thereby settled); D23 fires for `Special` only. Crawlers remapped per the cheat sheet (WW splits
  club out of `isSpecial` and reports the non-member shelf price; FS uses `bestPromotion` +
  `cardDependencyFlag`/`threshold`). Migration `20260717095635_AddPromoTypeModel` backfills
  `IsOnSpecial → PromoType=Special` for pre-model rows (re-typed on next crawl). API: listings + history points
  gain `promoType`/`memberPrice`(+ listings the multibuy pair); `/deals` = public specials only. Tests: 65
  crawling + 52 api green (new: WW club/multibuy fixtures, FS bestPromotion/club/member-multibuy fixtures, D3
  type-flip cases, /deals member-exclusion).
- **2026-07-19 20:47** — 🧑‍⚖️ *(Kevin)* **Per-run promo-distribution report**: every crawl run must end with the
  per-chain PromoType table (with each chain's loyalty-program name) in the log — see the "Per-run report" section.
  Implemented (`PromoReport` + `Chain.LoyaltyProgram()` + `report` CLI command) after the first full local crawl on
  the new model verified the mapping end-to-end (7 stores, 15,527 products, 4 data invariants at 0 violations,
  `/deals` = 2,591 = the Special count exactly).
- **2026-07-17 19:10** — **CORRECTION** (prompted by Kevin asking "non-members must pay *some* price — is it
  really not shown?"): **NW Clubcard deals publish BOTH prices.** `singlePrice.price` = the **non-member shelf
  price**, `rewardValue` = the **club price** — they differ on 423/423 card promos (e.g. eggs $12.59 shelf /
  $11.29 club). Public promos are the opposite: `singlePrice == rewardValue` (591/592) — the shelf price *is* the
  promo price and the pre-promo regular price is what's unpublished. So **D23 reconstruction is needed only for
  public specials**, not for the member axis; the member price needs its own field (`MemberPrice`) in the model.
  Also exposed a live inconsistency: for club deals our crawler stores the non-member price (`singlePrice`) yet
  flags `IsOnSpecial` — while the WW crawler stores `salePrice` (= the member price) — so today the same club-deal
  product carries opposite price semantics per chain.

## The problem (current state — evidence)

Today the model is **binary + a volatile tag**:

- **`Product.IsOnSpecial` (bool) + `Product.CurrentNonSpecialPrice` (the was-price)** — that's the entire "is this a
  deal" signal. No promo *type*, no start/end date. `/deals` = `IsOnSpecial && CurrentPrice != null` (was-price
  optional as of 2026-07-11).
- **`ProductTag` (D13)** — a chain-scoped m2m dimension that captures the source's promo *badge* (e.g. Woolworths
  "Low Price" = `tagType:"IsGreatPrice"`). But it's **volatile** (reset every crawl, not snapshotted, NOT part of the
  D3 price tuple) and **doesn't drive `/deals`**. So it holds a hint of "type" but isn't a first-class promo model.

**Confirmed gaps:**
- **Woolworths drops the member price.** `WoolworthsCrawler` maps **only** `isSpecial` → `IsOnSpecial`
  ([`src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs` ~L255-256](../../src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs)).
  The source **also** exposes **`isClubPrice`** (Everyday Rewards member/club price) — we **don't read it at all**, so
  member-only deals are invisible, and everything flagged `isSpecial` is treated as one undifferentiated bucket.
- **No start/end dates** are captured for any chain.
- **FreshChoice** (MyFoodLink, meat page recon) shows only temporary specials (`talker--Special`/`--Discount`/
  `--Saving` stickers + `was $X`); **no member price and no dates in the page HTML**. (Only one page recon'd — other
  pages/stores may carry "Every Day"/member/multibuy stickers; needs broader recon.)

## Cheat sheet — each chain's promo system at a glance (recon'd 2026-07-17)

The four banners run **three genuinely different promo systems**; NW and PAK share one platform (one API schema)
but use it with opposite strategies. All numbers from the 2026-07-17 archive scan.

| | **Woolworths** | **New World** | **PAK'nSAVE** | **FreshChoice** |
|---|---|---|---|---|
| Loyalty scheme | Everyday Rewards | Clubcard | none seen | none seen |
| **公开临时特价 → `Special`** | `isSpecial && !isClubPrice` (3,451) | `decal 3000`, `cardDependencyFlag=false` — only **7%** of NW promos | `decal 6000` — **100%** of PAK promos are public | `talker--Special` sticker + "was $X" |
| **会员价 → `MemberPrice`** | `isClubPrice` (942 — always also `isSpecial`, so WW itself mixes them) | `cardDependencyFlag=true` (`decal 4000/5000`) — **93%** of NW promos | none (0/1,735) | none seen |
| **Multibuy → `Multibuy`** | `productTag.multiBuy {quantity: 3, value: 20}` = "3 for $20" (869) | `threshold>1`, `rewardValue` = total for N; mostly Clubcard-gated (`decal 5000`) | `threshold>1` (145, public) | not seen yet |
| 常年低价 (not a promo type — D13 tag only) | "Low Price" = `tagType IsGreatPrice` | — | (the whole banner is EDLP-positioned) | "Every Day"? TBD |
| Was-price | **published** (`originalPrice`) | club deals: both prices published (`singlePrice` shelf / `rewardValue` club); public specials: regular unpublished → D23 | **not published** → D23 reconstruction | **published** ("was $16.80") |
| Promo dates | fields exist, **always null** | none | none | none |
| Price scope | national (1 store) | per-store | per-store | per-store |

Two structural quirks worth remembering:
- **WW**: `isClubPrice` ⊂ `isSpecial` — a club price is *also* flagged special, which is exactly why our `/deals`
  looked "all mixed together". Disambiguate club **first**, then what's left of `isSpecial` is the real public
  special. For a club deal, `salePrice` = member price and `originalPrice` = non-member shelf price.
- **NW** *(corrected 2026-07-17 19:10)*: a Clubcard deal publishes **both** prices — `singlePrice.price` = the
  **non-member shelf price** (what a cardless shopper pays), `rewardValue` = the **club price** (423/423 differ).
  A *public* promo is the opposite: the shelf price is already lowered (`singlePrice == rewardValue`, 591/592) and
  the pre-promo regular price is unpublished — **that** is what D23 reconstruction is for. Net: the "member $X vs
  shelf $Y" axis (decision C) is fully source-fed; D23 covers only the public-special was-price.

## Per-chain source signals — FULL recon (2026-07-17, from the D12 raw archive)

Scanned the local raw archive (D12 — crawlers keep every raw response on disk; runs of 2026-06-22/24):
**2,725 Foodstuffs `promotions[]` elements** (3 newest runs per banner) + **14,684 Woolworths products** (2 newest
runs). Findings supersede the trimmed-fixture notes from 2026-07-12.

### Foodstuffs (NW / PAK'nSAVE) — `promotions[]` element, full schema

`{ promoId, rewardValue (cents), decal, sapType, rewardType, threshold, limit?, multiProducts, description?,
cardDependencyFlag, bestPromotion, comparativePrice }` — **no date fields anywhere** (0/2,725).

- **`rewardType`**: only **`NEW_PRICE`** exists (2,725/2,725). Not the type discriminator we hoped.
- **`cardDependencyFlag`** = **needs the loyalty card → this is the MemberPrice signal.** 919/2,725 true — and they
  are **all New World** (Clubcard deals). **93% of NW promos are card-dependent** (919/990); PAK'nSAVE has none.
- **`threshold` + `rewardValue`** = **multibuy**: `threshold: 3, rewardValue: 500` = "3 for $5.00" (`rewardValue` is
  the **total for N**, not a unit price — the unit shelf price stays in `singlePrice.price`). `threshold > 1` in 181
  elements. `multiProducts: true` ≠ multibuy — it means the promo **spans an assorted range** (same `promoId` across
  e.g. 5 yoghurt flavours).
- **`singlePrice.price` vs `rewardValue`** (threshold ≤ 1): for **card promos they always differ** (423/423 —
  `singlePrice` = non-member shelf price, `rewardValue` = club price); for **public promos they're equal** (591/592
  — the shelf price *is* the promo price; one stray mismatch, likely a mid-update glitch). So the member axis is
  fully published; only the public special's pre-promo regular price is missing (→ D23).
- **`decal`** (badge id) decode: `3000` = NW non-club special · `4000` = NW Clubcard deal · `5000` = NW Clubcard
  multibuy · `6000` = PAK'nSAVE special · `7000` = rare (2 seen, undecoded). Consistent with `cardDependencyFlag`.
- **`description`** = purchase limits ("Limit 8 assorted"), not dates.
- **`sapType`**: `BASE` (2,436) vs `LAYERED` (289) — layered promos exist but `bestPromotion` already picks the
  primary.
- ⚠️ Current crawler ([`FoodstuffsCrawler.cs` ~L193](../../src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs)) sets
  `IsOnSpecial = promotions[] non-empty` and tags from `promotions.First()` (not `bestPromotion`) — so **NW
  club-only deals and multibuy-only promos are all presented as public specials today.**

### Woolworths — `price` object + `productTag`, full schema

- **`isClubPrice` is a strict subset of `isSpecial`** (942 club, all also special; 3,451 special-only; **zero**
  club-only) → confirms the original observation: WW mixes member prices into `isSpecial`, and we currently drop
  `isClubPrice`. Mapping is clean: `isClubPrice` → MemberPrice, `isSpecial && !isClubPrice` → Special.
- **`promotionStartDate` / `promotionEndDate` fields EXIST but are always `null`** in the browse API (0/14,684
  populated). The per-product *detail* endpoint might populate them, but that's +1 request per product (~3,400) —
  not worth it; note for a someday spot-check.
- **`productTag.multiBuy`** = structured multibuy: `{ quantity: 3.0, value: 20.0, link: "/shop/productgroup/…" }` =
  "3 for $20" (869 non-null; can coexist with `isSpecial`).
- **`productTag.tagType` vocabulary** (14,684 products): none 5,094 · `Other` 4,392 (dropped, D13) · `IsSpecial`
  3,159 · `IsClubPrice` 942 · `IsGreatPriceMultiBuy` 741 · `IsNew` 174 · `IsFreshDeal` 128 · `IsGreatPrice` 54.
- Other flags seen (all rare/unused so far): `hasBonusPoints`, `isTargetedOffer`, `isBoostOffer`, `savePercentage`.

### FreshChoice (MyFoodLink)

HTML stickers `talker__sticker--Special|Discount|Saving` + `talker--Special` card class + `was $X`. **No dates in
HTML.** Only one page recon'd — member/everyday/multibuy sticker variants TBD when the crawler is built.

**Takeaway:** every chain has a clean, decodable type signal (member + multibuy included) — but **no source
publishes promo dates** in any list/browse response. Start/end must come from our own observation history.

## Decided model (A–E settled 2026-07-17 — see Decision log)

**`PromoType` enum** on the price side; each crawler maps its native signal to it. **No separate `Promotion`
entity/aggregate** — that was the rejected option D2; type is a column on `Product` (current state) + a column in
the `PriceSnapshot` (history), exactly like `IsOnSpecial` today.

- **A (+A′ 2026-07-17)** — buckets: **`None · Special · MemberPrice · Multibuy`**. (`Multibuy` added once the recon
  decoded it for WW + FS; `EverydayLow` stays a D13 tag, not a promo type.)
- **B = B2** — `Product.PromoType` (denormalized current) **and** `PromoType` joins the D3 change-only tuple →
  `{Price, IsOnSpecial, NonSpecialPrice, UnitPrice, PromoType}`; a type flip (member→special at the same price)
  appends a snapshot.
- **C** — `/deals` = `Special` only; **member price is a separate axis** the UI shows beside the shelf price
  ("member $X"), never a "deal".
- **D = D1** — primary promo only (FS: the `bestPromotion` element — note today's crawler takes `First()`, fix
  that); all raw badges still flow into D13 `ProductTag`, full raw JSON in the D12 archive.
- **E** — recon done (see above): **no source publishes dates** (WW has the fields, always null). → derive
  **observed windows** from our own snapshot history: `IsOnSpecial`/`PromoType` flipping in a snapshot marks the
  observed start/end (±12h at twice-daily crawls); API can expose e.g. `onSpecialSince`. Don't add
  `PromoStartsAt`/`PromoEndsAt` columns until some source actually populates them (WW detail endpoint = a possible
  someday spot-check, +1 request/product so not for the crawl).

### Crawler mapping (per the recon)

| Chain | MemberPrice | Special | Multibuy |
|---|---|---|---|
| Woolworths | `isClubPrice` | `isSpecial && !isClubPrice` | `productTag.multiBuy {quantity, value}` |
| NW / PAK (best promo) | `cardDependencyFlag` | otherwise (`NEW_PRICE`) | `threshold > 1` (`rewardValue` = total for N) |
| FreshChoice | *(none seen)* | `talker--Special` sticker | TBD |

Precedence when signals coexist (D1, one primary type): **MemberPrice > Special > Multibuy > None** — the unit shelf
price is what we compare, and member/special describe that unit price while multibuy doesn't change it.

### Price-field semantics (added after the 19:10 correction)

`Product.CurrentPrice` must mean the same thing on every chain: **the unit price a cardless shopper pays today.**

| PromoType | `CurrentPrice` | new **`MemberPrice`** (nullable) | was-price (`CurrentNonSpecialPrice`) |
|---|---|---|---|
| `Special` | the discounted shelf price | null | source (`originalPrice`/"was $X") or D23 |
| `MemberPrice` | **non-member shelf price** (WW `originalPrice` · FS `singlePrice`) | WW `salePrice` · FS `rewardValue` | null (CurrentPrice isn't discounted) |
| `Multibuy` | regular unit price | null | null; deal captured as quantity+total (storage TBD at impl — e.g. `MultibuyQuantity`+`MultibuyTotal`, WW `{quantity,value}` / FS `threshold`+`rewardValue`) |

⚠️ This **changes today's WW mapping**: the crawler stores `salePrice` as CurrentPrice, which for a club deal is the
member price — while NW stores the non-member price. Same club-deal product, opposite semantics per chain; the new
model fixes the comparison. `MemberPrice` (and the multibuy pair) join the D3 tuple alongside `PromoType` (B2 —
member price changes weekly; a change must snapshot).

### Per-run report (🧑‍⚖️ 2026-07-19)

Every crawl ends by logging a **promo-distribution table** (`[report]` lines — scheduled `CrawlJob` and the
one-shot `crawl` command both; `dotnet run --project src/Zhua.Worker -- report` prints it ad-hoc without
crawling). Builder: [`Infrastructure/Crawling/PromoReport.cs`](../../src/Zhua.Infrastructure/Crawling/PromoReport.cs);
program names from `Chain.LoyaltyProgram()` (Domain). Purpose: a **mapping regression tripwire** — if a source
renames a promo signal (e.g. `cardDependencyFlag`), the matching column collapses toward 0 in the next run's log.

First real run (local, 2026-07-19):

```
chain       program                total    none special  member multibuy  promo%
Woolworths  Everyday Rewards        3398    2290     789     212      107    33%
NewWorld    New World Clubcard      4622    3512     150     953        7    24%
PaknSave    —                       7507    5796    1652       0       59    23%
TOTAL                              15527   11598    2591    1165      173    25%
```

Reading it: `special` = public deals (feeds `/deals` — 2,591 total matches the endpoint exactly); NW's promos are
~86% Clubcard-gated (953 member vs 150 public) while PAK'nSAVE runs **no loyalty program at all** (EDLP
positioning — every deal is public, hence `—`); WW mixes all three mechanics and has the highest promo share.
Baselines to eyeball against: NW `member` suddenly 0 → `cardDependencyFlag` broke; PAK `member` suddenly > 0 →
either PAK launched a program or our flag reading broke; `special` ≠ `/deals` total → predicate drift.

### Remaining questions

- ~~A′ — add `Multibuy` now?~~ **Approved 2026-07-17 18:58** (see Decision log).
- ~~The fate of `IsOnSpecial`?~~ **Settled at build time (23:41): kept as a stored column, narrowed to
  `PromoType == Special`**, derived inside `ApplyObservation` — the `/deals` predicate and the D23 guard read it
  unchanged, and member/multibuy listings are simply `false`.

## Interim (until this is built)

When the **FreshChoice** crawler is written, capture the raw sticker type (`Special`/`Discount`/`Saving`) into the
existing **`ProductTag` (D13)** dimension and keep `IsOnSpecial` + was-price as today — **so no information is lost**
and the richer model can be layered on later without re-crawling.

## Open questions

*(2026-07-17: the four original open questions are all resolved — B2 / C / recon "no dates" / see below —
leaving only:)*

- **A′** and the fate of `IsOnSpecial` (see "Remaining questions" above).
- **D23 interaction** (D23 = reconstructing NW/PAK's missing regular price from prior off-special history) —
  *(reframed by the 19:10 correction)*: D23 applies to **public specials only** (their `singlePrice` is the promo
  price and the regular price is unpublished). `MemberPrice` products need **no** reconstruction (both prices are
  in the source) and should **not** trigger D23 — check `ReconstructWasPrice`'s guard when implementing.
- **FreshChoice** member/everyday/multibuy sticker variants — recon during crawler build (D26 spec in
  [crawling.md](crawling.md)).

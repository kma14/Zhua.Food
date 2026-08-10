# Category alignment — mapping each chain's shelves onto the shared tree

**Status: PLAN. Nothing built, nothing decided.** Written 2026-08-10 at Kevin's request
("直接第四步,第四步要先计划好再实施") after the cheaper routes were measured and rejected.
This is rungs 0–2 of [ai-roadmap.md](ai-roadmap.md) applied to a small, safe problem.

## The problem, measured

Each chain names its shelves its own way, and `CategoryMapper` maps a non-Foodstuffs
`StoreCategory` onto the shared tree only by an **exact `(Kind, Slug)` match**. Woolworths
coverage:

| Kind | nodes | mapped | |
|---|---|---|---|
| Department | 5 | 5 | 100% |
| Aisle | 48 | 13 | 27% |
| Shelf | 232 | 55 | **24%** |

### Why only Woolworths has this problem

Measured 2026-08-11 over every chain's `StoreCategory` tree:

| chain | Department | Aisle | Shelf | mapped |
|---|---|---|---|---|
| New World | 12 / 12 | 101 / 101 | 418 / 418 | **100%** |
| PAK'nSAVE | 12 / 12 | 102 / 102 | 449 / 449 | **100%** |
| FreshChoice | 4 / 4 | — | — | no aisle/shelf tree at all (TD-7) |
| **Woolworths** | 5 / 5 | **13 / 48** | **55 / 232** | **24%** |

The shared tree was **seeded from the Foodstuffs taxonomy** (D22), so NW and PAK'nSAVE map by
*identity* — they cannot miss. Woolworths is the only chain that has a full, independently worded tree
of its own — so it is the only chain with anything to align, and adding a fifth chain later would hit
exactly this problem again. **177 = the Woolworths shelves that don't map.**

**FreshChoice has nothing to align, which is not the same as being fine.** All 4 of its nodes map; it
simply has no aisle/shelf nodes, because the crawler only reaches Department granularity (D26). Its
products still reach shelf level — but through the **item**, not through its own tree:
`CategoryMapper` gives each item the finest mapped category among its store products, preferring a
Foodstuffs member ([CategoryMapper.cs](../../../src/Zhua.Application/Matching/CategoryMapper.cs)). Measured
2026-08-11 over 1,260 FreshChoice listings:

| listing's item | listings | resulting category |
|---|---|---|
| has an NW/PAK member | 386 | **Shelf** — borrowed from the Foodstuffs member |
| has a Woolworths member only, whose WW shelf mapped | 24 | **Shelf** |
| has a Woolworths member only, whose WW shelf did *not* map | 39 | Department (36) / Aisle (3) ← the 177 |
| **is a FreshChoice singleton** (D30 Tier 4 — nothing to borrow from) | **387** | **Department only** ← TD-7 |
| does not exist — unmatched listing | 424 | none ← TD-5 |
| | **1,260** | |

So FreshChoice's gap is a **crawling** gap (fetch the finer tree from MyFoodLink's store-parameterised
CloudFront JSON) plus a **matching** gap, not a taxonomy-alignment one. Different fix, different rung —
it is not in scope here.

The two taxonomies differ in **wording and in shape**:

```
WW's Lamb shelves          Foodstuffs' Lamb shelves
  chops-cutlets      ←→      lamb-chops-cutlets        parent word not repeated
  roast-lamb         ←→      roast-lamb-large-cuts     extra qualifier
  fillet-steaks      ←→      lamb-steaks-fillets       word order flipped
  diced-stir-fry     ←→      lamb-mince-stir-fry       different words entirely
  lamb-sausages      ==      lamb-sausages             the only exact hit
```

Structurally, WW splits `mince-patties` and `sausages` into two aisles where Foodstuffs has
one `mince-sausages-meatballs`, and WW carries **merchandising aisles** (`3-for-20`,
`bbq-meat`, `roast-meat`) that are not categories at all and must map to **nothing**.

**User-visible effect:** a product is filed on the finest category that *mapped*, so
unmapped shelves strand their products on the aisle. Beef: 157 total, 103 across its
shelves, **54 stranded** (44 of them Woolworths). `?direct=true` (shipped 2026-08-10,
[api.md](../../api.md)) makes that remainder *reachable*; it does not make it *categorised*.

## Why the cheap routes were rejected

Measured over the 177 unmapped WW shelves:

| Route | Recovers | Verdict |
|---|---|---|
| **A. Parent-aware string rules** (front-end's proposal) — try `{parentSlug}-{slug}`, suffix match, prefix match | **13 / 177** | Rejected. Word order and vocabulary differ too much, and 98 of the 177 sit under an aisle that is itself unmapped, so a parent-anchored rule has nothing to anchor to. |
| **B. Content voting** — let the shelf's products (via their Foodstuffs-derived item category) vote | 84 / 177 at ≥60% | Rejected **as an auto-mapper**, kept as a **candidate generator**. Coverage looks good but the evidence is thin: 33 of the 84 rest on 1–2 voting products, and it produces confident nonsense — `spinach-greens-kale → broccoli-cabbage-cauliflower` (4/6), `roast-lamb → lamb-steaks-fillets` (3/3), `pork-mince-patties → pork-sausages` (2/2). |
| **C. Hand-curated alias table** | up to 177 | Viable but ~177 human judgements, and it re-rots whenever either chain reshuffles. |

The residual is a **taxonomy alignment** problem, not a string problem. That is what the
AI route is for.

## Design

### Shape: retrieve → judge → band

```
   for each unmapped StoreCategory
            │
            ▼
   ① embed + nearest-neighbour ──▶ top-5 candidate shared nodes  (+ "none")
            │
            ▼
   ② LLM judge: shelf + its parent path + 10 sample product names + the 5 candidates
            │                          ──▶ { path | null, confidence, reason }
            ▼
   ③ band by confidence:  ≥0.85 auto-apply · 0.5–0.85 review queue · <0.5 leave unmapped
```

**Honest note on ①:** at this scale retrieval is *not load-bearing* — the whole shared tree
is 193 nodes ≈ 2.5k tokens and fits in one prompt, so the judge could see all of it. The
retrieval step earns its place for two other reasons: it is rung 1 of the roadmap and the
piece that gets **reused** on the real problem (matching 19k listings, where the candidate
set can't fit in a prompt), and it produces a measurable number (recall@5) before any LLM
is involved. Building it here means learning it on 425 vectors where a wrong answer is
visible at a glance, instead of on 6,500 items where it isn't.

### Rung 0 first — the eval set (do this before any model)

Full design in [rung-0-eval.md](rung-0-eval.md). Problem-specific facts:

- **Free positives (73):** the shelves that already map by exact slug or department alias.
  Ground truth, but the *easy* cases — a smoke test, not the headline number.
- **Hand-labelled set: all 177 unmapped shelves**, each a shared path or explicitly `none`,
  collected through [label-tool.html](label-tool.html).
- **Baseline to beat:** 73 mapped, precision ~100%, recall ~29%.

### ① Retrieval

Embed a **path-qualified label**, not the bare name, so the parent disambiguates:

```
WW      "Meat & Poultry > Lamb > Chops & Cutlets"
shared  "Meat, Poultry & Seafood > Lamb > Lamb Chops & Cutlets"
```

- **Corpus:** 193 shared nodes + 232 WW shelves + 48 WW aisles ≈ **473 texts**, one-off.
- **Provider: still open** — Anthropic has no first-party embeddings endpoint, so it is a local
  in-process model vs a second vendor. Argued in [rung-1-embeddings.md](rung-1-embeddings.md).
- **Storage: plain `float[]` in memory, not pgvector.** 473 vectors is ~2 MB; brute-force cosine over
  that is microseconds. pgvector is the right answer at 19k+ items — adopt it on the matching problem,
  not here. Details in [rung-1-embeddings.md](rung-1-embeddings.md).
- **Constrain the search** to candidates under a mapped ancestor where one exists — cheap
  precision win, and it's the "parent-aware" idea the front-end asked for, done properly.

### ② The judge

Prompt shape, structured output, caching, confidence bands and cost live in
[rung-2-judge.md](rung-2-judge.md). Problem-specific points only:

- **Give it evidence, not just names:** the shelf's path, and ~10 sample product names from
  it. "Chops & Cutlets" containing *lamb loin chops, lamb forequarter chops* is a far easier
  call than the name alone.
- **`null` must be first-class** in the prompt and the schema. Explicitly tell it that
  promotional groupings (`3-for-20`, `bbq-meat`) and genuinely WW-only shelves have no
  counterpart and must return `null`.

### ③ Persisting the result — needs a schema change

`CategoryMapper` now **recomputes every mapping on every run** (changed 2026-07-27), so an
AI- or human-assigned mapping would be wiped by the next `match`. Before any of this can
land, `StoreCategory` needs:

```csharp
public MappingSource MappingSource { get; set; }   // Auto | Ai | Manual
```

with `CategoryMapper` owning only `Auto` rows and leaving `Ai`/`Manual` alone. `Manual`
outranks `Ai` outranks `Auto`. This is the same "don't clobber the human" guard the matcher
already has for approved `MatchCandidate`s.

Review queue: reuse the `MatchCandidate` shape — a `CategoryCandidate` row per middle-band
suggestion with the judge's `reason` and sample products, approved/rejected through the
existing admin API pattern.

### Layering

Consistent with `Zhua.Crawling` (also external I/O behind a port):

- **Port** `ICategorySuggester` → `Zhua.Application` (Domain-only deps, as required).
- **Adapter** → a new **`Zhua.Ai`** project referencing the `Anthropic` NuGet package,
  parallel to `Zhua.Crawling`. Never referenced by `Zhua.Api`.
- **Wiring:** `AddAi()` in `Worker/Program.cs` only. The Api must not gain an AI dependency,
  same rule as crawling/matching.
- **Runs offline**, as its own `dotnet run --project src/Zhua.Worker -- align-categories`
  command — *not* inside the crawl or the scheduled job. Category alignment changes only when
  a chain reshuffles its tree; running it twice a day would be pure waste.

## Cost

Per full run over 177 shelves, ~2.5k cached system + ~250 fresh input + ~150 output each:

| Model | $/MTok (in/out) | Estimated run cost |
|---|---|---|
| `claude-opus-5` | $5 / $25 | **≈ $1.10** |
| `claude-sonnet-5` | $3 / $15 | ≈ $0.65 |
| `claude-haiku-4-5` | $1 / $5 | ≈ $0.22 |

Embeddings: ~473 short texts, one-off — negligible whichever provider, but confirm current
pricing rather than assuming. The Batch API would halve the judge cost; not worth the added
latency and complexity at this size.

**The cost is irrelevant at this scale — accuracy is the only axis that matters.** Don't
pick a smaller model to save 90 cents.

## Rollout

1. **Rung 0** — label the hard set with Kevin; build `eval-categories`; score the current
   exact-slug mapper as the baseline. *No model yet.*
2. **Schema** — `MappingSource` + migration, so results have somewhere to live that survives
   a `match`.
3. **Rung 1** — embeddings + in-memory cosine; report **recall@5** on the labelled set. If
   recall@5 is poor the judge can't recover it, so this gate comes first.
4. **Rung 2** — the judge over rung 1's shortlist; score precision per confidence band on the
   holdout; **pick the auto-apply threshold from the measured curve, not from a guess.**
5. **Ship** — auto-apply the top band, queue the middle, leave the rest unmapped. Re-run
   `align-categories` manually; re-score whenever either chain reshuffles.
6. **Then Foodstuffs (TD-10)** and FreshChoice (TD-7) fall out of the same machinery — the
   whole point of building it generically.

## Risks

- **Wrong auto-mappings are worse than no mapping.** They put real products under the wrong
  shelf, which is exactly the class of bug the 2026-07-27 identity fix just cleaned up. Hence
  precision-first thresholds and a conservative default band.
- **A model that maps everything** scores well on positives and destroys the tree. The
  negative labels in the eval set are the only guard — don't skip them.
- **An eval built only on the free 73** measures the easy half and will mislead.
- **Provider dependency** for embeddings adds a second vendor and a key to manage; a local
  ONNX model avoids it at the cost of quality and setup. Open question below.
- **Both trees keep moving.** This is a periodic re-run, not a one-shot; budget for that.

## Open questions for Kevin

1. ~~**Embedding provider**~~ — **answered 2026-08-10 🧑‍⚖️: local ONNX, in-process.** See § ① Retrieval.
2. ~~**Skip rung 1?**~~ — **answered 2026-08-10 🧑‍⚖️: no, rung 1 stays.** Picking a local embedding
   provider is a decision to *build* the retriever; the reuse-on-matching argument carries it.
3. ~~**Who labels the hard set?**~~ — **answered by making it cheap:** the sheet is generated with
   candidates pre-filled (route B votes + route A hits + 8 sample product names per shelf), so it is a
   multiple-choice pass, not free-typing paths → [label-tool.html](label-tool.html).
   Kevin ticks the boxes. **Still open inside this:** 44 stratified shelves, or all 177 (which also yields
   route C, a shippable alias table, as a by-product)?
4. **Auto-apply at all**, or route every AI suggestion through the review queue for the first
   run and only enable auto-apply once the measured precision justifies it? — **still open.** Does not
   block rung 0; decide it from the measured precision-per-band curve rather than up front.

## Decision log

- 2026-08-11 00:05 *(answering Kevin: "为啥只有 ww 的，其他超市没有类似的毛病吗")* Measured every chain's
  tree and added **[Why only Woolworths has this problem](#why-only-woolworths-has-this-problem)** — NW and
  PAK'nSAVE are 100% mapped because the shared tree was seeded from their own taxonomy (D22), FreshChoice
  has no aisle/shelf tree to align (TD-7). **Woolworths-only is a fact about the data, not a sampling
  choice.** Follow-up (Kevin: "那 FC 的产品是怎么 map 到 shelf 的") — traced and tabulated in the same
  section: FreshChoice listings reach shelf level **through the item, not through their own tree**, so
  its gap is a crawling + matching gap, not an alignment one. Side-finding worth keeping: **39 FreshChoice
  listings are collateral damage of the 177** — they sit in Woolworths-anchored items whose WW shelf is
  unmapped, so labelling the 177 lifts them too. [label-tool.html](label-tool.html) rebuilt in the same pass: the two halves of each question
  are now explicitly labelled 题目 / 答案, and shared-tree candidates show their **display name** (with the
  slug as subtext) so both sides of the comparison read the same way — previously a WW display path was
  being compared against a bare slug. Answers already in `localStorage` are preserved (same key).
- 2026-08-10 23:30 🧑‍⚖️ *(Kevin)* **Method content moved out to the per-rung docs**
  ([rung-0-eval.md](rung-0-eval.md), [rung-1-embeddings.md](rung-1-embeddings.md),
  [rung-2-judge.md](rung-2-judge.md)); this doc keeps the problem, the measurements, the rejected routes
  and the rollout. **The hard set is all 177 shelves, not a 44-shelf sample** — it removes sampling bias
  and yields route C (the hand-curated alias table this doc costed at "~177 human judgements") as a
  by-product, which fixes the front-end's stranded-products complaint without any model. Labels are
  collected through a clickable local page ([label-tool.html](label-tool.html)) and stored as a CSV file
  in the repo. **Correction:** the entry below recorded "embedding provider = local ONNX" as settled;
  that was a mis-capture and is withdrawn — the provider is open, argued in
  [rung-1-embeddings.md](rung-1-embeddings.md).
- 2026-08-10 🧑‍⚖️ *(Kevin: AI 的落点选「分类对齐」)*
  **This plan is now the ratified entry point for the AI work**, ahead of the matching problem itself —
  it is the same eval→retrieve→judge shape at 1/40th the scale, and its retriever is what gets reused on
  matching later. Two of the four open questions are closed with it: **embedding = local ONNX in-process**
  (no second vendor, no second key, no data leaving the box; the judge still runs on the Claude API), and
  therefore **rung 1 is built, not skipped**. Q3 (who labels) is answered by generating the sheet as
  multiple choice — [label-tool.html](label-tool.html), 44 stratified shelves
  with route-A/route-B candidates and sample products pre-filled. Q4 (auto-apply) stays open by design:
  it should be read off the measured precision curve, not guessed. **Still no model code, no dependency,
  no migration** — the next artifact is Kevin's labels, then `eval-categories` scoring the current
  exact-slug mapper as the baseline. Live counts re-verified against the DB the same day: 232 WW shelves
  / 55 mapped, 48 aisles / 13 mapped, 193 shared nodes, 98 of the 177 unmapped shelves under an unmapped
  aisle — every figure in this doc still holds.
- 2026-08-10 21:55 🧑‍⚖️ *(Kevin: "先做第一步,然后直接第四步,第四步要先计划好再实施")* Plan written
  after step 1 (`?direct=true`) shipped. Routes A (13/177) and B (84/177 but demonstrably
  wrong at small sample sizes) measured and rejected as auto-mappers before proposing AI —
  B is retained as a candidate generator for labelling. Scope deliberately covers Woolworths
  first, with Foodstuffs (TD-10) and FreshChoice (TD-7) as follow-ons from the same machinery.
  Two deliberate divergences from [ai-roadmap.md](ai-roadmap.md) rung 1, both because of
  scale: no pgvector (473 vectors — brute-force cosine in memory), and retrieval is justified
  by reuse + measurability rather than necessity. **Nothing decided or built** — four open
  questions above.

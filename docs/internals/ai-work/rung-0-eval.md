# Rung 0 — the eval harness

**Status: ground truth being collected; no code yet.** The scoreboard every later rung is scored on.
Method doc — the problem it is first applied to is [category-alignment.md](category-alignment.md).
Ladder context: [ai-roadmap.md](ai-roadmap.md).

> **The one rule this rung exists to enforce: no AI change ships without a score.**
> Skip it and every later decision — which prompt, which threshold, which model — is decided by
> "看着还行". That is the failure mode this rung is here to make impossible.

## Why this comes before any model

Five concepts, each solving a specific problem we would otherwise hit:

**1. Ground truth.** Every "is this good?" question in ML has to be anchored to answers a
human gave. Without it, "这个映射看着挺对" is unverifiable, unrepeatable and uncomparable.

**2. Baseline.** You must know **what today scores** or you cannot tell whether 70% is a triumph
or a regression. Here the baseline is the current exact-slug mapper: high precision, low coverage. The
AI has to beat **both** numbers, not just one.

**3. Precision vs recall — and here they are not symmetric.** This is the crux for this project:

| | consequence |
|---|---|
| **A wrong mapping** | real products are filed under the wrong shelf — a shopper browsing lamb chops sees milk. Exactly the class of bug the 2026-07-27 Woolworths id-recycling fix just cleaned up. |
| **A missing mapping** | the product stays on its aisle, still reachable via `?direct=true`. Status quo. |

**One wrong mapping is worse than ten missing ones.** So the headline metric is **precision on
auto-applied mappings**, recall is secondary, and the auto-apply threshold gets set from the measured
precision-per-confidence-band curve — never guessed up front.

**4. Negatives are not filler.** A model that always picks the closest-looking node scores beautifully on
positives and quietly destroys the tree — it maps `3 for $20` to a real category. The only thing that
catches that behaviour is having labelled `none` cases in the set. Roughly a fifth of the labels are
expected to be `none`; that is a feature.

**5. Sampling bias — and why the free labels are not enough.** The 73 shelves that already map by exact
slug *are* ground truth, but they are the easy half; scoring only on them overstates everything. Hence
the labels are drawn from the **177 that don't map**.

**6. The circularity trap.** We cannot score a matcher against labels that matcher produced — that is
grading your own exam. This is why the labels must come from a human independently, and why this rung
cannot be automated away. (It is also the open question the roadmap raises for the *matching* problem,
where `Approved`/auto-linked pairs are the heuristic's own output.)

## The ground truth

**Produced by** [label-tool.html](label-tool.html) — a self-contained local page listing all 177 unmapped
Woolworths shelves, each with pre-computed candidates (route-A string hits, route-B content votes) and 10
real product names as evidence. Progress autosaves to `localStorage`; the export button emits CSV.

**Woolworths only, and that is not a sample.** NW and PAK'nSAVE map at 100% (the shared tree *is* their
taxonomy) and FreshChoice has no shelf-level tree to align — coverage table in
[category-alignment.md](category-alignment.md#why-only-woolworths-has-this-problem).

**Stored as a file in the repo, not in the DB** (decided 2026-08-10 🧑‍⚖️). A label is a human judgement —
it belongs with source code, not with crawled data. Concretely: it survives a DB rebuild, it diffs in
review, and it needs no migration.

```csv
ww_shelf,answer
meat-poultry/lamb/chops-cutlets,meat-poultry-seafood/lamb/lamb-chops-cutlets
meat-poultry/3-for-20/3-for-20-meat,none
```

`answer` is either a shared-tree `Category.Path` or the literal `none`. `ww_shelf` is the WW
`dept/aisle/shelf` slug path — stable, and the same key the mapper works in.

**Scope: all 177** (decided 2026-08-10 🧑‍⚖️), not a 44-shelf sample. This removes sampling bias entirely
and yields route C — the hand-curated alias table [category-alignment.md](category-alignment.md) costed at
"~177 human judgements" — as a by-product that could ship without any model.

## What the harness computes

`dotnet run --project src/Zhua.Worker -- eval-categories`

Scores **any** mapper against the label file. Reported per run:

| metric | definition | why |
|---|---|---|
| **precision** | of the mappings it asserted, how many match the label | the headline — wrong mappings are the expensive error |
| **recall** | of the labels that are a real path, how many it found | coverage; secondary |
| **`none` accuracy** | of the labels that are `none`, how many it correctly left unmapped | catches a map-everything model |
| **per-band precision** | the above, split by the judge's confidence | this is what the auto-apply threshold is read off |

The first thing it must print is the **baseline**: today's exact-slug mapper, so we always know what we
are beating.

## Where it lives

Rung 0 needs **no AI and no external call** — it is arithmetic over the DB plus a CSV.

```
Zhua.Application/CategoryAlignment/
    ICategoryAlignmentEval.cs      port (Domain-only deps, per D27)
    CategoryAlignmentEval.cs       use case: read labels, run mapper, compute metrics
Zhua.Worker/Program.cs             + "eval-categories" CLI, alongside crawl / match / report
```

`Zhua.Api` does not participate — the architecture test enforces that. No entity change and **no
migration**: `MappingSource` is only needed once AI results are *written back*, which is rung 2's problem.

## Status / next

1. ⬜ **Kevin labels 177 shelves** in [label-tool.html](label-tool.html) → export CSV — *blocking*
2. ⬜ commit the CSV as the ground truth file
3. ⬜ build `eval-categories`, print the baseline
4. → then [rung-1-embeddings.md](rung-1-embeddings.md)

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`), then 🧑‍⚖️ if user-instructed.

- **2026-08-10 23:30 — 🧑‍⚖️ (Kevin)** Doc split out per rung at Kevin's request ("每个 rung 单独开个文档"),
  method separated from the problem it is applied to. Three decisions recorded here: **ground truth lives
  in its own file** (not a DB table); **all 177 shelves get labelled**, not a 44-shelf sample, because it
  removes sampling bias and hands us route C for free; labelling is done through a **clickable local
  page** rather than by ticking Markdown by hand.

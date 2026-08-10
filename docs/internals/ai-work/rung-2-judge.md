# Rung 2 — LLM as judge

**Status: designed, not built.** Method doc — applied to
[category-alignment.md](category-alignment.md) first, and to matching in
[item-matching-judge.md](item-matching-judge.md). Ladder context: [ai-roadmap.md](ai-roadmap.md).

## The shape

The model is never asked an open question. It is handed a **shortlist** (from
[rung-1-embeddings.md](rung-1-embeddings.md)) plus evidence, and asked to make a **bounded choice** —
including an explicit "none of these" escape so it is never forced into a wrong answer:

```
shortlist  +  evidence  ──▶  { path | null, confidence, reason }
                                              │
          confidence bands:  high → auto-apply · mid → review queue · low → leave unmapped
```

Everything below is about making that call cheap, parseable, and honest about its own uncertainty.

## Concepts this rung is about

**Structured output.** Ask for prose and you own a parser and a retry loop. Instead, declare a JSON
schema on the request (`output_config.format`, type `json_schema`) and the response is guaranteed to
conform:

```jsonc
{ "targetPath": "meat-poultry-seafood/lamb/lamb-chops-cutlets" | null,
  "confidence": 0.0-1.0,
  "reason": "one sentence" }
```

**Prompt caching.** The rules + the shared-tree block are byte-identical across all 177 calls (~2.5k
tokens). Marking that prefix with `cache_control` makes every call after the first bill those tokens at
~0.1×. The mechanic worth internalising: **caching is a prefix match** — one changed byte anywhere in the
prefix invalidates everything after it, so stable content goes first and the per-shelf part goes last, in
the user turn.

**Confidence bands and thresholds.** The model returns a self-reported confidence; that number is only
useful once it has been *calibrated* against rung 0's labels. So the flow is: run the judge, plot
precision per band, then pick the auto-apply line from the measured curve. **Never guess the threshold
up front** — that is the difference between an eval-driven system and a vibes-driven one.

**"None" as a first-class answer.** The schema allows `null` and the prompt states explicitly that
merchandising groupings (`3 for $20`, `bbq-meat`) and genuinely chain-specific shelves have no
counterpart. A judge that always finds something is the failure mode rung 0's negative labels exist to
detect.

**Evidence beats names.** Give it the shelf's full path *and* ~10 real product names from inside it.
"Chops & Cutlets" containing *lamb loin chops, lamb forequarter chops* is a far easier call than the name
alone — and it is the same reason the human labelling page shows product names.

## Model & cost

**`claude-opus-5`.** At this volume the price difference between models is cents, so there is no reason to
trade accuracy for cost. Thinking is on by default on this model.

Per full run over 177 shelves (~2.5k cached system + ~250 fresh input + ~150 output each):

| model | $/MTok (in/out) | est. run cost |
|---|---|---|
| `claude-opus-5` | $5 / $25 | **≈ $1.10** |
| `claude-sonnet-5` | $3 / $15 | ≈ $0.65 |
| `claude-haiku-4-5` | $1 / $5 | ≈ $0.22 |

**Cost is irrelevant at this scale — accuracy is the only axis that matters.** Don't pick a smaller model
to save 90 cents. (The Batch API would halve it again; not worth the latency and complexity here. On the
*matching* problem, where volume is 100× higher, this arithmetic changes and is re-done in
[item-matching-judge.md](item-matching-judge.md).)

## Where it lives

```
Zhua.Application/CategoryAlignment/
    ICategorySuggester.cs        port: shelf + candidates → { path?, confidence, reason }
                                 (Application depends on Domain only — no Anthropic type may appear here)
Zhua.Ai/                         new project, parallel to Zhua.Crawling
    ClaudeCategorySuggester.cs   adapter: prompt assembly, Anthropic SDK, schema, caching
Zhua.Worker/Program.cs           .AddAi() + "align-categories" CLI
```

Three constraints, inherited from the crawling precedent and D27:

- **`Zhua.Api` never references `Zhua.Ai`.** Same treatment as crawlers; the architecture test enforces it.
- **Never inside the scheduled crawl job.** Taxonomies only move when a chain reshuffles; running this
  twice a day is pure waste. It is a manual command.
- **Non-destructive.** The judge only *adds* mappings or proposes them; it never overwrites a human
  decision.

## The schema change this rung needs

`CategoryMapper` recomputes every mapping on every run (changed 2026-07-27, deliberately — that is what
makes it self-healing). So an AI- or human-assigned mapping would be wiped by the next `match`. Before
anything here can land:

```csharp
public MappingSource MappingSource { get; set; }   // Auto | Ai | Manual
```

with `CategoryMapper` owning only `Auto` rows. **`Manual` outranks `Ai` outranks `Auto`.** Same
"don't clobber the human" guard the matcher already has for approved `MatchCandidate`s. This needs a
migration — but only at this rung; rung 0 needs no schema change at all.

Middle-band suggestions reuse the `MatchCandidate` shape: a `CategoryCandidate` row carrying the judge's
`reason` and sample products, approved/rejected through the existing admin API pattern.

## Open

- **Auto-apply at all on the first run**, or route every suggestion through the review queue until the
  measured precision justifies it? Deliberately unanswered — it should be read off the band curve.
- Whether `reason` is persisted for every decision or only for queued ones (audit vs storage).

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`), then 🧑‍⚖️ if user-instructed.

- **2026-08-10 23:30 — 🧑‍⚖️ (Kevin: "每个 rung 单独开个文档")** Split out of
  [category-alignment.md](category-alignment.md), which keeps the problem statement and measurements and
  now links here for the judge's design. Model, cost table, structured-output shape and caching approach
  carried over unchanged; the `MappingSource` schema requirement is restated here because it belongs to
  this rung, not to rung 0.

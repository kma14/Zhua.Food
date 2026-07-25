# zhua.food — AI roadmap (discussion, nothing decided)

> **Status: DISCUSSION — nothing here is ratified, nothing is being built yet.** Kevin's instruction
> (2026-07-23): "AI 的部分单独开个文档，要好好讨论再进行" — open a dedicated doc for the AI work and **discuss it
> thoroughly before proceeding**. So everything below is a **proposal / option**, not a rule. Decisions move to the
> Decision log (with 🧑‍⚖️) only once agreed. Don't treat any table row as approved.
>
> This is the **umbrella roadmap**. The existing [ai-matching.md](ai-matching.md) is *one rung* of it (the
> LLM-as-judge design) and stays the detail doc for that rung. Surrounding context: the matcher itself
> [matching.md](matching.md), why unmatched listings persist [orphan-matching.md](orphan-matching.md), the item
> model [item-model.md](item-model.md).

## Two goals, on purpose

This work is driven by **two** goals at once — keep both in view when we make choices:

1. **Practical AI experience (Kevin's primary intent).** Get hands-on with as many *real* AI concepts as possible —
   embeddings, vector search, RAG, LLM structured output, evals, active learning, (later) fine-tuning / multimodal —
   on real, messy data instead of a toy tutorial.
2. **A genuinely better matcher.** Every rung must also ship real value to zhua.food, so the learning isn't
   throwaway. The front-end's report (无品牌生鲜 / `Broccoli` 分裂 / 肉类称重) is the concrete pain we're aiming at.

Where the two goals ever conflict (e.g. "fine-tune an embedding model" is great learning but overkill for value),
we say so explicitly and let Kevin choose. **Learning-per-effort and value-per-effort are both first-class.**

## Why matching is the right vehicle

Item matching **is** *entity resolution / record linkage* — a canonical ML problem — and it decomposes cleanly into
three sub-problems, each a different family of AI concepts:

```
召回 (candidate generation / "blocking")  →  判定 (ranking / matching)  →  人在环 (active learning)
        embeddings, ANN, 语义相似度              LLM judge, 分类器, 结构化输出        review queue, uncertainty sampling
```

We already have the two scarcest ingredients:

- **Real dirty data** — 4 chains, divergent wordings, missing brands, weight-sold produce.
- **Human labels** — every auto-linked + `Approved` `MatchCandidate` is a positive pair; every `Rejected` is a
  negative. That labelled set is what makes *evals*, *RAG few-shot*, and (later) *fine-tuning* possible without
  hand-building a dataset.

## Guiding principles (proposed — to confirm)

- **Eval-first.** No AI change ships without a score. Build the scoreboard (rung 0) before any model.
- **AI augments, never replaces.** The heuristic matcher stays as the **baseline & fallback** (它现在的 0.8/0.3
  阈值就是第一个被 eval 打分的对象). AI is additive to the cascade in [matching.md](matching.md), same as D30's tiers.
- **Non-destructive & human-confirmed** (inherited from [ai-matching.md](ai-matching.md)): AI only *adds*
  links/candidates, never unlinks or rewrites owned item text; uncertain picks still go to the human queue; a
  `Rejected` pair is never re-proposed.
- **Clean Architecture holds.** All of this is **offline matching** — lives in `Zhua.Application/Matching`, runs in
  the **Worker**, **never the Api** (Api only reviews). External calls (embeddings, LLM) go behind an **Application
  port** with an **Infrastructure adapter**, exactly like the repository ports (D27). Vectors live in Postgres.
- **Cheap by construction.** Shortlist-then-judge, prompt-cache the system prompt, run AI only on *unlinked*
  listings — the cost math in [ai-matching.md](ai-matching.md#cost--affordable-because-we-shortlist) already holds.

## The ladder (proposal — order and scope both open)

Each rung = a set of concepts to learn + a real deliverable + its own open questions. Later rungs depend on earlier
ones, so this is a pipeline, not a menu.

| # | Build | AI concepts | Deliverable |
|---|---|---|---|
| **0** | **Eval harness** — score any matcher against a holdout built from existing labels (`Approved`/auto-linked = positive, `Rejected` = negative). `dotnet run -- eval`. | evals, train/test split, **precision/recall tradeoff** (false-merge ≫ false-split), baseline | Every change (incl. today's heuristic) gets a number. The scoreboard for all rungs below. |
| **1** | **Embeddings + pgvector** — vectorise `Item.Description` / listing name; add the `pgvector` extension to our existing Postgres; cosine-similarity **retrieval** replaces the `brand+size+category` hard filter for the shortlist. | **embeddings, vector DB / ANN, 语义相似度, blocking** | `Broccoli` ≈ `fresh vegetable broccoli head` — directly attacks the no-brand-produce split. Big recall gain. |
| **2** | **LLM-as-judge** — the existing [ai-matching.md](ai-matching.md) design: shortlist (from rung 1) → Haiku-class model → structured `{itemId, confidence, reason}`. | **prompt engineering, structured output / tool use, prompt caching, cost/token 权衡, confidence thresholds** | Cross-store judgement beats the heuristic; review queue pre-filled with the AI's pick + reason. |
| **3** | **RAG for matching** — retrieve already-confirmed similar pairs as **few-shot** examples in the judge prompt. | **RAG, few-shot, retrieval-augmented judgement** | Judge improves as labels accumulate, no prompt edits. |
| **4** | **Small classifier (optional but very practical)** — logistic regression over features (name-sim, brand-match, size-match, category-match, embedding-cos) → probability. The cheap/fast control group for the LLM. | **feature engineering, 经典 ML 分类, 阈值校准, 可解释性** | A near-zero-cost stable matcher to benchmark the LLM against. |
| **5** | **Active-learning loop** — the review queue, closed: model surfaces the *most uncertain* pairs for humans, re-run/retrain, watch rung 0's score climb. | **active learning, human-in-the-loop, uncertainty sampling, data flywheel** | Human labelling spent where it's worth most; system improves with use. |
| **6** | **Multimodal (advanced)** — product images (Foodstuffs derives image URLs D24; WW has them) → image embeddings as a disambiguation signal. | **多模态, image embedding, 信号融合** | Image tie-break when names are ambiguous. |
| **7** | **Fine-tune embeddings (top tier)** — contrastive fine-tune a sentence-transformer on our confirmed pairs so "same item" sits closer in vector space. | **fine-tuning, contrastive learning, 领域适配** | Another step up in retrieval quality. |

## Recommended entry point (proposal)

**Don't skip rungs. Do 0 → 1 → 2, fully, before anything else.**

- **0 first** or everything downstream is vibes — and it's the concept most people never practise.
- **1 is the highest learning-density rung *and* hits the current real bug** (the hard filter dies when brand/size
  is missing; vector retrieval is the textbook fix, and our Postgres makes ops ~free).
- **2 is already designed** ([ai-matching.md](ai-matching.md)) and drops straight onto rung 1's shortlist.

Rungs 4–7 are deliberately parked as "learn more later" — flag when we get there.

## Open decisions — to settle in discussion before building

These are the "好好讨论" items. **None are decided.**

1. **Scope of this pass.** Just rungs 0+1 as a first concrete design? Or plan 0→2 up front? (Recommendation: design
   0+1 in detail now, keep 2 as the already-written follow-on.)
2. **Eval dataset shape.** Which labels count as ground truth (auto-linked pairs are the heuristic's *own* output —
   circular if we score the heuristic against them)? Do we need a small **hand-labelled** golden set independent of
   the matcher? How big? Who labels it?
3. **Embedding model.** Local (ONNX / sentence-transformers, no API cost, more ops) vs. an API embedding model
   (simpler, per-call cost, data leaves the box)? Which one? Dimensionality vs. pgvector index choice.
4. **pgvector rollout.** New migration + extension; where the vector column lives (on `Item`? a side table?); how it
   stays fresh across crawls; index type (ivfflat/hnsw) and when it's worth it at our catalogue size.
5. **Does rung 1 replace or sit beside the heuristic hard filter?** (Principle says *augment* — likely a widened
   shortlist that the heuristic/judge still gates, not a replacement.)
6. **Cost & privacy posture for any API calls** — model choice, prompt caching, what data is allowed to leave.
7. **Where the learning lives vs. the product.** Some rungs (4, 7) may be Python/notebook experiments off to the
   side rather than in the .NET pipeline. Decide per rung whether it lands in `Zhua.*` or stays exploratory.

## Relationship to existing docs

- [ai-matching.md](ai-matching.md) — **rung 2** detail (LLM judge). Its "Open decisions" section overlaps this one;
  when we ratify, decisions land there for rung 2 and here for the overall shape.
- [matching.md](matching.md) — the current heuristic cascade (D30) that stays the **baseline**; AI augments it.
- [orphan-matching.md](orphan-matching.md) — the wider reality check: ~72% of Woolworths orphans have *no* match to
  find, so **AI is not a silver bullet** for the unmatched-listings count; it helps the *judgeable* subset. Read
  before assuming a rung "solves" browsability.
- [item-model.md](item-model.md) — the item-as-join-key model the matcher operates on.
- [tech-debt.md](tech-debt.md) — the multipack size-equivalence miss (matching.md gotcha, 2026-07-23) is a concrete
  case rung 1/2 could pick up.

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-23 — 🧑‍⚖️ (Kevin: "AI 的部分单独开个文档，要好好讨论再进行")** Opened this discussion doc as the umbrella
  AI roadmap. Everything in it is a **proposal, not a decision** — the explicit instruction is to discuss thoroughly
  before building. The staged ladder, the "eval-first / AI-augments-not-replaces" principles, and the "0→1→2 first"
  recommendation are Claude's proposal for that discussion, pending Kevin's ratification. No code, no dependencies,
  no migration started.

---

*Discussion doc. Ratified decisions graduate to this Decision log (+ [ai-matching.md](ai-matching.md) for rung 2);
implementation starts only after that.*

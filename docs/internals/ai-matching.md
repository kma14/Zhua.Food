# zhua.food — AI-assisted matching (design, deferred)

> **Status: NOT implemented — deferred.** **For now, matching is manual:** today's heuristic matcher
> (`brand + size + name-overlap`, see [matching.md](matching.md)) auto-links the confident cases and drops the rest
> into the **human review queue**, resolved via the admin endpoints (`approve` / `reject` / `link-item` /
> `create-item`). This doc is the plan for when we *augment* that heuristic with an LLM. Surrounding model
> (why items exist, `description`-as-anchor): [item-model.md](item-model.md).
>
> **Scope note:** this doc covers the LLM as a **review-queue judge** (Category D in
> [orphan-matching.md](orphan-matching.md)). That research doc is the wider picture — it shows the review queue is
> only one of four orphan buckets, that ~72% of Woolworths orphans have *no* match to find (so AI can't help them),
> and that the real prerequisite is de-anchoring items from Foodstuffs. Read it before assuming "add AI" solves the
> unmatched-listings problem.

## Why AI

The heuristic — name-token overlap given equal brand+size — is brittle when store wordings diverge
("mainland cheese colby" vs "Smooth & Creamy Colby Cheese") and when a new banner's naming hasn't been hand-tuned.
An LLM judges *semantic* sameness far better and scales to stores we haven't curated for. It's the natural fit for
the platform's core judgement: "are these the same item?"

## Where it slots in (the flow doesn't change)

Per store product, every match run (from [item-model.md](item-model.md#matcher-direction-additive-ai-assisted)):

1. **Already linked?** → leave it.
2. **Deterministic key?** (Foodstuffs branches share `productId`) → link, no AI.
3. **Otherwise → AI.** ← *this document*

AI is **only step 3, and only on unlinked listings.** It does **not** replace the human review queue — it *feeds* a
better candidate into it. Confident picks link; uncertain ones still go to a human. Non-destructive throughout.

## The call: shortlist, then pick

1. **Retrieve candidates — cheap, no LLM.** Pre-filter items by `brand + size + category` → a handful of
   plausible matches. (At larger catalogue scale, swap this step for embedding similarity over `description`.)
2. **Ask the LLM to choose.** Prompt = the store listing + each shortlisted candidate's `description`; the model
   returns a structured choice — including an explicit "none of these → it's new" escape so it's never forced into a
   wrong link.

**Output schema:**
```json
{ "itemId": "…" | null, "confidence": 0.0, "reason": "…" }
```

**Decision thresholds** (tunable; start conservative, bias to the queue, loosen as we earn trust):

| Confidence | Action |
|---|---|
| high (≥ auto-link bar) | link automatically |
| medium | → review queue (existing `MatchCandidate`), with the AI's pick + `reason` pre-filled |
| low / `null` | propose a **new item** (its `description` seeded from the listing), pending review |

## Cost — affordable, because we shortlist

The driver is **candidates-per-prompt, not the model**:
- Shortlist → a few hundred input tokens + a tiny JSON answer, on a **cheap/fast model (Haiku-class)**.
- AI runs **only on unlinked listings** — steps 1–2 absorb the bulk for free (Foodstuffs is deterministic).
- **One-time backfill** (the cross-store ambiguous set, ~Woolworths) ≈ **single-digit dollars**.
- **Steady state** = only *new* listings per crawl (tens–hundreds) ≈ **pennies per crawl**.
- **Anti-pattern:** all items in every prompt (~50–60k tokens/product) — 100× worse. **Cache the system
  prompt**; if the catalogue ever explodes, move the shortlist step to embeddings retrieval.

## Model & prompt notes

- A cheap/fast **Claude** model (classification-grade); use **structured output / tool use** for the JSON.
- The system prompt (rules + schema) is constant → **prompt-cache** it across the batch.
- Keep candidate `description`s short; include brand/size/category to disambiguate.
- Always include the "none → new" option.

## Guardrails

- **AI proposes, humans confirm** the uncertain ones — never auto-link below the high-confidence bar.
- **Non-destructive:** AI only *adds* links/candidates; it never unlinks or rewrites a item's `description`.
- **Respect prior human decisions** — a rejected pair is never re-proposed (same as today's matcher).
- **Auditability:** persist the AI's `reason` on the `MatchCandidate`.

## Open decisions

- Model choice + structured-output mechanism (tool use vs. JSON mode).
- Auto-link confidence bar; whether to ever auto-create new items or always queue them.
- Embeddings (which) for retrieval at scale; batch backfill vs. per-crawl incremental.
- An **eval set** — a hand-labelled sample to measure precision/recall before we trust auto-linking.

---

*Deferred design. Ship the manual review flow first; turn this on when review-queue volume justifies it.*

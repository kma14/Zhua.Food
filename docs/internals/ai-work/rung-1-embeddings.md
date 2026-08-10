# Rung 1 — embeddings & retrieval

**Status: to build after [rung-0-eval.md](rung-0-eval.md); provider still open.** Method doc — first
applied to [category-alignment.md](category-alignment.md). Ladder context: [ai-roadmap.md](ai-roadmap.md).

## What an embedding actually is

A model that turns a piece of text into a fixed-length list of numbers (a **vector**), positioned so that
texts with similar *meaning* land near each other. Similarity is then just geometry — **cosine
similarity** between two vectors.

That is the whole idea, and it is what string matching cannot do:

```
"Meat & Poultry > Lamb > Chops & Cutlets"                →  [0.21, -0.05, 0.88, ...]
"Meat, Poultry & Seafood > Lamb > Lamb Chops & Cutlets"  →  [0.19, -0.04, 0.91, ...]   ← close
"Fruit & Vegetables > Vegetables > Carrots"              →  [-0.6,  0.31, 0.02, ...]   ← far
```

The embedding model is **not an LLM**. It does not chat, reason, or decide — it only encodes. Typical
size is 20–300M parameters (tens to hundreds of MB), versus hundreds of billions for a frontier model.

### Two things a vector is not

**1. It is not a universal encoding of meaning.** A vector is a coordinate *on one particular model's
map*, and it is meaningless anywhere else. Different models produce different dimensionalities
(384 / 768 / 1024) so they cannot even be compared; and two same-dimension models still have unrelated
axes, so a cosine across them is noise. **It will not throw** — it quietly returns a plausible-looking
number, which is what makes this the nastiest available foot-gun. Hence the one hard rule:
**corpus and query must be encoded by the same model, same version.** It is also why the vectors are not
worth persisting — swap the model and every stored vector is garbage.

**2. It is not something an LLM consumes.** Vectors are never sent to Claude; the Messages API takes text
(and images), not float arrays. Rung 1 hands rung 2 the **names** of the top candidates:

```
inside rung 1:   text ──▶ vector ──▶ cosine ──▶ ranked ──▶ top-5
rung 1 output:   the 5 candidate paths, as plain text
                       │
                       ▼
rung 2 prompt:   "shelf 'Chops & Cutlets' contains lamb loin chops, lamb forequarter chops…
                  candidates: ① Lamb Chops & Cutlets ② Lamb Steaks & Fillets …
                  pick one, or answer none"
```

**The vector disappears at the handoff.** It is an internal intermediate of this rung and never leaves it.

## corpus, query — and what is deliberately *not* in the corpus

Standard information-retrieval vocabulary. Every retrieval system has two sides:

| term | what it is | ours |
|---|---|---|
| **corpus** | the fixed set of texts you encode up front and search against | the **193 shared-tree nodes** — the option pool |
| **query** | the one text you search *with*, at request time | **one Woolworths shelf**, 177 times |

A search engine's corpus is the web; a chatbot-RAG's corpus is your document store; ours is a **ballot**.

**The ground-truth labels are not the corpus and are never retrieved.** Embedding the answer key and
searching it would score ~100% and mean nothing — that is the circularity trap from
[rung-0-eval.md](rung-0-eval.md). The three roles stay strictly separate:

| | what it is | role |
|---|---|---|
| 193 shared-tree nodes | corpus | searched — the top-5 comes from here |
| 177 Woolworths shelves | queries | one lookup each |
| **177 human labels** | **ground truth** | **scoring only — never touched during retrieval** |

## How the comparison actually works

Vectors are normalised to length 1, so **cosine similarity is just the dot product**: multiply
element-wise, sum. Four dimensions for legibility (real ones are 384):

```
query   "Chops & Cutlets"        q  = [0.6, 0.8, 0.0, 0.0]
corpus  "Lamb Chops & Cutlets"   c₁ = [0.5, 0.8, 0.3, 0.1]
corpus  "Cream Cheese"           c₂ = [0.1, 0.1, 0.7, 0.7]

cos(q,c₁) = 0.6×0.5 + 0.8×0.8 + 0.0×0.3 + 0.0×0.1  ≈  0.94   ← close
cos(q,c₂) = 0.6×0.1 + 0.8×0.1 + 0.0×0.7 + 0.0×0.7  =   0.14   ← far
```

Then: 1 query vector × 193 corpus vectors → 193 scalars → sort → take 5. So yes, the final step is one
number compared against another — but **each number is 384 dimensions voting at once**. Geometrically it
is the angle between two arrows in 384-dimensional space (`1.0` same direction, `0.0` orthogonal); once
normalised every arrow sits on the unit sphere, so angle and distance are the same question.

**That is the point of the whole rung:** it converts "do these two phrases mean the same thing", which is
hard, into "which float is bigger", which is trivial. The hard part was paid for during model training.

### In .NET, do not hand-roll the loop

The loop above is the explanation, not the implementation. `System.Numerics.Tensors` (first-party .NET,
**not** third-party) ships a SIMD-accelerated primitive — verified against the worked example above on
version 10.0.10:

```csharp
using System.Numerics.Tensors;

var top5 = corpus
    .Select(c => (c, score: TensorPrimitives.CosineSimilarity(queryVec, c.Vector)))
    .OrderByDescending(t => t.score)
    .Take(5);
```

```
TensorPrimitives.CosineSimilarity(q, c₁) = 0.9447
TensorPrimitives.CosineSimilarity(q, c₂) = 0.1400
```

## Why retrieval comes before the judge

Three reasons, weakest to strongest:

**1. Scale — which does not apply here.** The usual argument is that the corpus cannot fit in a prompt.
Ours can (see the honest note below). It *will* apply on the matching problem: 19,000 listings compared
pairwise is 3.6×10⁸ pairs.

**2. Retrieval sets the judge's ceiling.** If the right answer is not in the top-5, the judge never sees
it and cannot recover it. `recall@5` is therefore a **gate**: if it reads 70%, the whole system is capped
at 70% and no amount of prompt tuning moves it. Measure it before writing a single prompt.

**3. It is the only stage that can be scored without an LLM — and that makes failure attributable.**
This is the real engineering argument. A single end-to-end call that performs badly tells you nothing
about *why*. Two stages tell you exactly where to work:

| recall@5 | final precision | diagnosis |
|---|---|---|
| 92% | 60% | candidates are fine — the judge is choosing badly → prompt / evidence |
| 55% | 90% | the judge is sound — the ballot is incomplete → text representation / model |

Splitting the pipeline is what turns "it works" into "it can be improved". Same instinct as rung 0's
no-score-no-ship rule, one level down.

## Embeddings vs retrieval vs RAG

These are nested, not alternatives:

```
embedding   a model that turns text into a vector          — a component
    ↓ used for nearest-neighbour lookup
retrieval   pulling the most relevant few out of many      — a step
    ↓ the results are placed in a prompt, and a model generates from them
RAG         Retrieval-Augmented Generation                 — an architecture
```

`embedding ⊂ retrieval ⊂ RAG`. Retrieval does **not** require embeddings — BM25, SQL `LIKE`, and the
project's current exact-slug rules are all retrieval, just weak ones.

**So what we are building is structurally RAG** — but not the chatbot kind, and the difference is
load-bearing:

| | chatbot RAG | ours (category alignment) |
|---|---|---|
| what is retrieved | document chunks | **the answer options themselves** |
| role of the result | evidence / context | **the ballot** |
| model output | free text | a constrained choice: one path, or `null` |
| cost of a miss | vaguer answer — **soft degradation** | the right answer is not on the ballot — **hard ceiling** |

The accurate name for ours is **retrieval-augmented classification** (entity linking), and that is why
`recall@5` is a hard gate for us where it is merely informative in a chatbot.

**Note that the roadmap's "RAG" is rung 3, and it is a different use of the same technique** — retrieving
already-confirmed similar *cases* to inject as few-shot examples:

```
rung 1:  embed(the category tree)   → similar candidate nodes  → prompt them as the BALLOT
rung 3:  embed(the labelled cases)  → similar precedents       → prompt them as EXAMPLES
         ↑ same retriever, different corpus, different job
```

Rung 3's payoff is that the judge improves as labels accumulate without touching the prompt — which is
also why the 177 labels are not a single-use artifact. Same machinery, different problem: hence two rungs.

## Honest note: this problem does not need it

The shared tree is 193 nodes ≈ 2.5k tokens. **It fits in one prompt.** The judge in
[rung-2-judge.md](rung-2-judge.md) could simply be shown the entire tree and asked to pick. Retrieval is
*not load-bearing* at this scale.

It is being built anyway, deliberately (2026-08-10 🧑‍⚖️, Kevin: "这个项目的学习意义大于项目本身，所以只要
makes sense 的，就要用"). Two real returns:

1. **It is the piece that gets reused.** On the matching problem — 19,000 listings — the candidate set
   *cannot* fit in a prompt, so retrieval stops being optional. Learning it here means learning it on 473
   vectors where a wrong answer is obvious at a glance, instead of on 19k where it is not.
2. **It produces a number before any LLM is involved** — reason 3 above.

The cost of building it here is bounded and known: three packages and a few hundred lines.

## Provider — the open question

**Anthropic has no embeddings endpoint.** The Claude API only does Messages (conversation / judgement).
So "use Claude for embeddings too" is not an available option, and the real choice is:

| | what it is | for | against |
|---|---|---|---|
| **A. Local model, in-process** | a small open-source encoder run through `Microsoft.ML.OnnxRuntime` | no vendor, no key, no per-call cost, nothing leaves the machine; **teaches the most** — tokenisation, pooling, normalisation, cosine, storage are all yours | a model file to ship (and into the Worker container); quality below a commercial API |
| **B. Third-party API** | Voyage AI — the provider Anthropic's own docs point at | one HTTP call, best quality, nothing to package | a second vendor, a second key to manage, data leaves the box; **teaches the least** — it is one more REST call |

**Recommendation: A.** Given the stated goal is learning, the option that hides the mechanics behind an
HTTP call is the wrong one — the parts B hides (tokenising, pooling token vectors into one sentence
vector, normalising, cosine) *are* the lesson. It is also free and adds no vendor.

### Which local model — the shortlist, and how to choose

There are thousands of embedding models on HuggingFace. What narrows it to a handful is **our**
constraints, not the field:

| constraint | what it eliminates |
|---|---|
| must run in a .NET process | needs an ONNX export **and** a tokenizer available in .NET. `Microsoft.ML.Tokenizers` handles BERT-family WordPiece best — which is the real reason MiniLM / BGE / GTE keep coming up, not that they are the best models |
| size | the Worker image already carries Playwright + Chromium + self-contained .NET 9 |
| very short text | our inputs are ~10 tokens (`Meat & Poultry > Lamb > Chops & Cutlets`); an 8,192-token context window is paid for and unused |
| symmetric English similarity | we compare shelf-name ↔ shelf-name, not query ↔ document |
| permissive licence | commercial use |

Viable candidates, by size:

| model | params | dims | note |
|---|---|---|---|
| `all-MiniLM-L6-v2` | 22M | 384 | the classic baseline; ONNX exports everywhere; cheapest |
| `bge-small-en-v1.5` | 33M | 384 | better quality at the same tier |
| `gte-small` | 33M | 384 | same tier, different lineage |
| `e5-small-v2` | 33M | 384 | **requires `query:` / `passage:` prefixes** — omit them and it degrades silently |
| `snowflake-arctic-embed-s` | 33M | 384 | same tier |
| `bge-base-en-v1.5` | 109M | 768 | clearly better, 5× the size |
| `nomic-embed-text-v1.5` | 137M | 768 | Matryoshka (dims can be truncated) |
| `mxbai-embed-large-v1` | 335M | 1024 | |
| `Jina v5-text-small` | 677M | — | "small" by 2026 standards but 20–30× the above; ONNX/.NET availability unverified |

Anything larger (`Qwen3-Embedding-8B`, `Harrier-OSS`) is server-class and will not fit the Worker.

**Selection rule: measure, do not copy the leaderboard.** MTEB averages 50+ datasets — scientific
retrieval, financial QA, tweet classification — and **none of them is NZ supermarket shelf taxonomy**.
A model 2 points higher on MTEB can easily be worse on our 473 short strings. We will have something the
leaderboard does not: 177 hand-labelled examples and a metric that matters to us.

```
shortlist 3–4 across the size range  →  run each  →  recall@5 on our own labels  →  pick from the number
```

Proposed shortlist: `all-MiniLM-L6-v2` (floor, 22M), `bge-small-en-v1.5` (33M), `gte-small` (33M,
different lineage), `bge-base-en-v1.5` (109M — is 5× the size worth it?). This is rung 0's first real
payoff: the ruler exists before the model is chosen. Caveat when reading any published comparison:
**MTEB v2 scores are not comparable to v1**, and much of the writing online mixes them.

## Running a Python-trained model in .NET — why ONNX

Every one of these models is trained in Python/PyTorch; our pipeline is .NET. Three ways across:

| approach | cost |
|---|---|
| Python sidecar service | another process, another language in the repo, another deploy surface — on a Worker image that is already heavy |
| hosted API | that is option B — a vendor, a key, data leaving the box |
| **export to ONNX, run in-process** | one NuGet, no extra service ✅ |

**ONNX = Open Neural Network Exchange**, a portable **model file format** plus a runtime that executes
it — the interchange format for "trained in framework A, run in language B". Most mainstream embedding
models ship an ONNX export already.

**Is ONNX Microsoft's?** Half. Worth separating, because the `Microsoft.` prefix misleads:

| | ownership |
|---|---|
| **the ONNX format** (spec) | started by Microsoft + Facebook (Meta) in 2017, governed since 2019 by the **Linux Foundation** (LF AI & Data). An open standard, not Microsoft's |
| **ONNX Runtime** (execution engine) | **is** a Microsoft open-source project (MIT) — hence the package name `Microsoft.ML.OnnxRuntime` |

Also: `Microsoft.ML.OnnxRuntime` is **not ML.NET**, despite the shared namespace prefix. They are
separate projects.

So the dependency footprint for option A is three packages plus a model file (~90 MB at the MiniLM tier),
with **no API key, no network call, no third-party vendor**:

| package | job | owner |
|---|---|---|
| `Microsoft.ML.OnnxRuntime` | run the model: tokens → vector | Microsoft (OSS) |
| `Microsoft.ML.Tokenizers` | tokenise: text → tokens, using the model's own vocabulary | Microsoft (OSS) |
| `System.Numerics.Tensors` | cosine similarity + ranking | .NET first-party |

## Design (as applied to category alignment)

**Embed a path-qualified label, not the bare name** — the parent is what disambiguates:

```
WW      "Meat & Poultry > Lamb > Chops & Cutlets"
shared  "Meat, Poultry & Seafood > Lamb > Lamb Chops & Cutlets"
```

- **Corpus:** 193 shared nodes + 232 WW shelves + 48 WW aisles ≈ **473 texts**, computed once.
- **Storage: a plain `float[]` in memory — not pgvector, and not a file.** 473 vectors is ~2 MB, computed
  in a few hundred ms at startup, and brute-force cosine over it is microseconds. Persisting them buys
  nothing and creates an invalidation problem (change the model → every stored vector is wrong).
  **pgvector becomes the right answer on the matching problem** (19k+ items, repeated queries) — that is
  where that concept gets learned, and this doc deliberately diverges from the roadmap's
  "rung 1 = embeddings **+ pgvector**" for that reason.
- **Constrain the search** to candidates under an already-mapped ancestor where one exists. Cheap
  precision win, and it is the "parent-aware" idea the front-end asked for, done properly.
- **Metric: `recall@5`** on rung 0's labels, reported before any LLM work starts. Shelves labelled `none`
  are excluded from it — there is no correct node to retrieve; they are scored at rung 2 as `none` accuracy.

## Where it lives

Same port/adapter shape as `Zhua.Crawling` — external machinery behind an Application port:

```
Zhua.Application/CategoryAlignment/
    ICategoryRetriever.cs        port: shelf → top-N candidate paths + scores
Zhua.Ai/                         new project, parallel to Zhua.Crawling
    OnnxEmbedder.cs              the model + tokeniser + cosine
    EmbeddingCategoryRetriever.cs
Zhua.Worker/Program.cs           .AddAi() — the only place it is registered
```

**`Zhua.Api` never references `Zhua.Ai`** — same rule as crawling. `Zhua.Application` stays Domain-only,
so no ONNX type may leak into it.

## Open

- Provider A vs B (recommendation above; A unless Kevin objects).
- If A, which model — decided by measuring `recall@5` on rung 0's labels across the proposed shortlist,
  not by leaderboard rank.
- Whether the model file is committed, downloaded on build, or baked into the Worker image.

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`), then 🧑‍⚖️ if user-instructed.

- **2026-08-11 00:40 — 🧑‍⚖️ (Kevin: "把我们 cover 的知识写到 embedding 的文本里")** Session's discussion
  written up. New/expanded: **what a vector is not** (model-specific, never sent to an LLM — it disappears
  at the rung 1→2 handoff); **corpus vs query**, and the explicit statement that **the labels are not the
  corpus** (circularity); **how the comparison works** (cosine = dot product, worked 4-dim example,
  384 dims → one scalar); **use `TensorPrimitives.CosineSimilarity`, not a hand-written loop** — verified
  on `System.Numerics.Tensors` 10.0.10 against the worked example; **why retrieval precedes the judge**,
  with failure-attribution as the strongest reason; **embedding ⊂ retrieval ⊂ RAG**, why ours is really
  retrieval-augmented classification, and how rung 1 differs from rung 3's RAG (same retriever, different
  corpus); **why ONNX** and the format-vs-runtime ownership split (Linux Foundation vs Microsoft's ORT);
  the three-package footprint. Also replaced the two-model line with a **nine-model shortlist plus the
  selection rule** — answering "为什么本地模型只有俩，不是很多吗": the field is huge, our constraints (ONNX +
  a .NET tokenizer, image size, ~10-token inputs) are what narrow it, and the choice is made by measuring
  `recall@5` on our own labels rather than by MTEB rank. **Still nothing built and no decision taken** —
  provider and model both remain open.
- **2026-08-10 23:30 — 🧑‍⚖️ (Kevin: "不跳过 embedding，这个项目的学习意义大于项目本身")** **Rung 1 is
  built, not skipped**, overriding the recommendation to skip it on this problem (the shared tree fits in
  one prompt, so retrieval is not load-bearing here). Reason accepted and recorded: learning value is the
  primary goal, and this is the component that gets reused on matching where it *is* required. An earlier
  log entry recorded "provider = local ONNX" as settled — that was a mis-capture; the provider question
  is **open**, argued above, recommendation A.

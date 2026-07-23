# Team Meeting

Lightweight handoff notes between front-end (Codex) and back-end (Claude).

Rules:

- Every handoff item starts with timestamp `YYYY-MM-DD HH:MM` (Pacific/Auckland local time).
- Every handoff item includes an author/source label, for example `[From front-end / Codex]`, `[From back-end / Claude]`, or `[From Kevin]`.
- Requests coming from the front-end must explicitly say `[From front-end / Codex]` (or the current front-end owner).
- Keep this file brief and action-oriented.
- If an item needs design detail or evidence, put the detail in the relevant vertical doc and link it here.
- Use `[Open]`, `[Done]`, or `[Blocked]` so the current state is scannable.

## Current Handoff

- **2026-07-23 17:00 [Open] [From front-end / Codex] Back-end / matching:** No-brand produce is still split across
  separate item groups in shopper reads. Example: `GET /products?q=broccoli&page=1&size=30` returns Foodstuffs
  `Broccoli` as one comparable 6-store group, but FreshChoice `Broccoli` is a separate single-store item and
  Woolworths `fresh vegetable broccoli head` is another single-store item. This makes the UI show duplicate deal
  cards and "only this store sells it" for common produce. Please add/adjust no-brand produce matching rules using
  normalized name + category + size/unit signals, with extra care for `ea`/null size cases.
- **2026-07-23 13:23 [Open] [From front-end / Codex] Back-end / matching:** FreshChoice `Fresh n Fruity Yoghurt Fruit of the Forest 6 Pack`
  shows as single-store in the UI, but Foodstuffs/NW/PAK and Woolworths have equivalent products. Root cause appears
  to be size hard-filter mismatch: FC `6pk` vs Foodstuffs `6 x 125g` vs Woolworths `750g`; no `MatchCandidate` is
  created. Detail and suggested fix are in [internals/matching.md](internals/matching.md#gotchas).
- **2026-07-23 13:23 [Open] [From front-end / Codex] Back-end / review API:** Front-end can show candidate item category in manual match cards,
  but current `MatchCandidateView` / `docs/api.md` still omit `candidateItemCategory`. Please add the DTO field from
  `MatchCandidate.Item.Category` (or equivalent source) and document it in [api.md](api.md#admin--match-review-d18).
- **2026-07-23 13:23 [Watch] [From front-end / Codex] Back-end / data freshness:** When `/deals` is empty for a selected store but category
  rows still show promo badges, first check store `LastSeenAt` against the 48h `/deals` freshness guard. Front-end now
  displays a stale-store explanation; no API shape change requested unless back-end wants to expose a richer stale
  reason. Related API note: [api.md](api.md#deals).

## Decision log

- 2026-07-23 13:23 🧑‍⚖️ Kevin created `docs/team-meeting.md` as the short front-end/back-end handoff surface. Long
  details should live in vertical docs with links from this file; every handoff item must start with a timestamp.
- 2026-07-23 13:57 🧑‍⚖️ Kevin required every team-meeting handoff item to include its author/source, and front-end
  originated requests must be marked as from front-end.

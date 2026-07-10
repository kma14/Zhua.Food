# zhua.food — "near me" / location feature design

Rank and filter stores (and therefore price comparisons + deals) by how far they are from the shopper — so the app can
answer "where is X cheapest **near me**", not just "where is X cheapest across all of Auckland".

## Provenance (read this first)

Same convention as the other plan docs: separate what the user (Kevin) actually decided from proposals/options, so
later sessions don't treat a suggestion as a rule.

- 🧑‍⚖️ **= user-instructed** (a real decision Kevin made, dated). Treat as a rule.
- everything else = proposal / option / implementation detail — change freely.

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-10 22:20** — 🧑‍⚖️ *(Kevin)* Distance must be from the shopper's **actual current location** and be **real
  travel distance** — not straight-line (haversine), and not just a picked suburb centroid. Straight-line misranks in
  Auckland (harbour / motorways: close as the crow flies ≠ close to drive).
- **2026-07-10 22:20** — Open: routing service = self-hosted **OSRM** vs **Google Distance Matrix** (cost vs
  convenience — see below). Not yet decided.

## What already exists (verified in code)

- `GET /stores` already returns each store's `Latitude` / `Longitude`
  ([`StoreView`](../../src/Zhua.Application/Stores/Dtos/StoreView.cs)) — so the front-end has store coordinates today,
  no backend change needed for that part.
- Store coordinates are seeded precise per branch (used already for the Foodstuffs geolocation crawl resolution).

Everything else below is new work.

## Two hard prerequisites for "current location + real distance"

The user's requirement (current location + real travel distance) pulls in two things that a simpler suburb-based
version would not need:

### 1. HTTPS (required to read the current location at all)
Browsers only expose `navigator.geolocation.getCurrentPosition()` in a **secure context** — `https://` or `localhost`.
The NAS served as `http://<ip>:3000` will have geolocation **blocked**. So the deployed site must be HTTPS:
- DSM native path: **Control Panel → Security → Certificate**, free **Let's Encrypt** cert.
- A domain: **Synology DDNS** (`xxx.synology.me`, free) or a real domain.
- **DSM Reverse Proxy** (Login Portal → Reverse Proxy): `https://xxx.synology.me` → internal `web:3000`.
- Local dev is fine over `http://localhost:5173` (localhost is a secure context) — only the NAS deployment needs the cert.

### 2. A routing service (for real travel distance/time)
Haversine only gives straight-line. Real driving distance/time needs a routing engine. Two options (**open decision**):

| Option | Setup | Cost | Notes |
|---|---|---|---|
| **Self-hosted OSRM** | Run an OSRM container on the NAS + a NZ OpenStreetMap extract (small, a few hundred MB) | **Free, unlimited, no external key** | Fits a cost-sensitive project already running NAS containers. Watch NAS RAM (6 GB shared with the headed-Chromium worker). |
| **Google Distance Matrix** | Google Cloud project + API key + billing; backend proxy hides the key | Per-element (~$5–10 / 1000), has a free tier | Easiest, most accurate, includes live traffic. |

Whichever is chosen, the **API key / routing call lives on the backend** — never the front-end.

## End-to-end flow

```
① Browser getCurrentPosition() -> current lat/lng          (needs HTTPS)
       v
② Front-end haversine-prefilters all ~9 stores -> nearest 3-5   (free, cost gate)
       v
③ Front-end sends { current lat/lng, those storeIds } to the backend
       v
④ Backend calls the routing service (OSRM or Google):
   origin = current location, destinations = those stores -> real distance/time
       v
⑤ Front-end shows "12 min drive / 6.4 km", reranks by travel time
```

**Haversine is not discarded** — because the origin is arbitrary (the live current location, which can't be
precomputed/cached like a fixed suburb), every request is live. So haversine is the **cost gate** in step ②: cheaply
cut 9 stores down to the nearest few, and only ask the routing service about those.

Reference haversine (front-end, km):
```ts
function distanceKm(a: {lat:number,lng:number}, b: {lat:number,lng:number}) {
  const R = 6371, toRad = (d:number) => d * Math.PI / 180;
  const dLat = toRad(b.lat - a.lat), dLng = toRad(b.lng - a.lng);
  const s = Math.sin(dLat/2)**2 +
            Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLng/2)**2;
  return 2 * R * Math.asin(Math.sqrt(s));
}
```

## Front-end / backend split

| Part | Front-end | Backend |
|---|---|---|
| Read current location (`getCurrentPosition`) | ✅ (needs HTTPS) | — |
| Haversine prefilter to nearest N | ✅ | — |
| Store coordinates | uses `GET /stores` | ✅ already exposed |
| **Real travel distance** | calls a backend endpoint | ✅ **new**: proxy OSRM / Google + cache |
| **HTTPS on the NAS** | — | ✅ **new**: DSM cert + reverse proxy |
| "Near me" ranking/filter applied to price compare + deals | ✅ (uses the distances) | later: optional server-side distance filter if store count grows |

## Proposed backend endpoint (sketch — not built)

A thin travel-distance endpoint the front-end calls after its haversine prefilter:

```
POST /stores/travel-distance
{ "lat": -36.78, "lng": 174.75, "storeIds": ["…","…","…"] }
->
[ { "storeId": "…", "distanceMeters": 6400, "durationSeconds": 720 }, … ]
```
Backend forwards to OSRM/Google, hides the key, and may cache. (Route/paths TBD when it's built — keep this a sketch,
not a contract, until implemented + tested.)

## Phasing (proposal)

Each phase ships independently so the feature isn't blocked on HTTPS + routing all at once:

1. **Phase 1 — haversine only, pure front-end.** Pick a location (a suburb centroid is enough to start, no HTTPS
   needed), straight-line rank/filter stores + price compare + deals. Free, fast, validates that people use "near me".
2. **Phase 2 — current location + real travel distance.** Add HTTPS (DSM cert + reverse proxy) so `getCurrentPosition`
   works, add the routing service (try OSRM first; fall back to Google), add the backend travel-distance endpoint.

> Note: Phase 1's suburb picker is a stepping stone, **not** the end state — 🧑‍⚖️ the target is current-location real
> distance (Phase 2). Phase 1 exists only so the feature can ship before HTTPS + routing are in place.

## Open decisions to settle before Phase 2

- **Routing service: OSRM (self-host, free) vs Google Distance Matrix (paid, easy)?**
- **HTTPS domain: Synology DDNS (`*.synology.me`) vs a real domain?**
- Whether "near me" filtering stays client-side or moves server-side (only matters once store count grows well past
  the current ~9).

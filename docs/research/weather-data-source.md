# Weather data source and forecast granularity

Research for [issue #20](https://github.com/jpjerkins/task-guide/issues/20). Planning only —
nothing built.

**Scope basis.** Weather was ruled in by
[#9](https://github.com/jpjerkins/task-guide/issues/9) and is settled in `CONTEXT.md` §Dimension:
a categorical Dimension whose Window side is **fetched**, never authored — current conditions at
fire time, forecast for future-Window ranking and Scarcity, bounded to the active Pattern's week.
Evaluation is **lazy** (no weather-tagged Active Task ⟹ no API call) and **fails closed**, with a
footer note when the moment is UI-visible.

**Evidence basis.** Provider docs and terms pages as of **2026-08-08**. Where a documentation page
was silent or unreachable, I went to the live API and captured actual responses; those are marked
**[live probe]** and are the strongest evidence in this document, because they show what the API
returns rather than what the docs claim. Probes were run from this Macbook against a Columbus, OH
coordinate (39.9612, -82.9988).

---

## Bottom line

**Use Open-Meteo.** It is the only candidate that clears every constraint without a caveat, and it
clears them by a wide margin rather than narrowly.

Three findings drove this, in order of weight:

1. **NWS's forecast text is prose, not a vocabulary.** This is the surprise. `api.weather.gov`
   looks like the natural fit — free, US government, no key — but its `/forecast` endpoint's
   condition field is English sentences including compound phrases like
   `"Chance Showers And Thunderstorms then Partly Cloudy"` **[live probe]**. There is no code, no
   enum, nothing to switch on. Bucketing that into `{dry, wet, snow}` means string-matching
   free text that NOAA is free to reword. See [§3](#3-condition-granularity).
2. **OpenWeatherMap's no-card free tier tops out at 5 days**, one full day short of the week
   horizon Scarcity needs. Reaching 7+ days means the One Call subscription, which is a
   pay-as-you-call product. See [§1](#1-candidates-and-free-tier-terms).
3. **Open-Meteo returns current conditions, 7 daily rows and 168 hourly rows in a single HTTP
   call, with no API key** **[live probe]**, and its conditions are **WMO code 4677**, a numeric
   international standard that buckets to `{dry, wet, snow}` with a `switch` and no guesswork.

The rate-limit question in the issue turns out not to be a question at all: see
[§4](#4-rate-limits-versus-actual-cadence). Under the lazy rule this service will make on the
order of **10² calls/day worst case against a 10⁴/day allowance** — three orders of magnitude of
headroom. Caching matters for latency and politeness, not for staying under a limit.

**One genuine obligation, not a blocker:** Open-Meteo's data is CC-BY 4.0 and attribution is
required. See [§6](#6-the-attribution-obligation) — this is a one-line change to a UI surface the
domain already has.

---

## 1. Candidates and free-tier terms

### Open-Meteo — recommended

| | |
|---|---|
| **API key** | **None.** "No API key is required for free use." The free tier calls `open-meteo.com` directly; paid customers get a key for `customer-api.open-meteo.com` ([pricing](https://open-meteo.com/en/pricing)). Confirmed by **[live probe]** — an unauthenticated request returned `HTTP/1.1 200 OK`. |
| **Limits** | "Less than 10'000 API calls per day, 5'000 per hour and 600 per minute" ([terms](https://open-meteo.com/en/terms)). The pricing page adds a monthly figure of 300,000. |
| **Free forever?** | Yes — a standing tier, not a trial. |
| **Eligibility** | "You may only use the free API services for non-commercial purposes," with the examples explicitly including **"Utilizing our service for personal home automation purposes"** and "private or non-profit websites or apps that do not have subscriptions or advertising" ([terms](https://open-meteo.com/en/terms)). |
| **Licence** | CC-BY 4.0; "attribution is a requirement under this licence" ([pricing](https://open-meteo.com/en/pricing)). See [§6](#6-the-attribution-obligation). |
| **Coverage** | Global. |

The eligibility wording is worth dwelling on, because it is unusually favourable. Most providers'
"non-commercial" clauses are written to be argued about. Open-Meteo names *personal home
automation* as a positive example. A single-user, Tailscale-only, self-hosted task reminder is
that example almost verbatim — there is no ambiguity to manage here and no judgement call to
revisit later.

The stated caveat is availability, not entitlement: the free tier "carries no uptime guarantee"
and Open-Meteo "reserve[s] the right to block applications and IP addresses that misuse our
service without prior notice" ([terms](https://open-meteo.com/en/terms)). The domain already
handles this correctly — a failed fetch fails closed and surfaces in the footer. **No uptime
guarantee is not a problem for a system whose unavailable-weather behaviour is already settled.**

### US National Weather Service (`api.weather.gov`)

| | |
|---|---|
| **API key** | None. A `User-Agent` header identifying the application is required: "A User Agent is required to identify your application," e.g. `User-Agent: (myweatherapp.com, contact@myweatherapp.com)" ([docs](https://www.weather.gov/documentation/services-web-api)). |
| **Limits** | Undocumented in numbers — only "reasonable rate limits in place to prevent abuse," with exceeded requests retryable "typically within 5 seconds" ([docs](https://www.weather.gov/documentation/services-web-api)). **Gap: I could not find a published numeric limit anywhere first-party.** |
| **Free forever?** | Yes. "All of the information presented via the API is intended to be open data, free to use for any purpose." No commercial restriction at all. |
| **Coverage** | **United States only.** A hard dependency, though not a live constraint for this deployment. |

On terms alone NWS is the *most* permissive candidate — genuinely public-domain, no
non-commercial clause to satisfy, no attribution obligation. It loses on data shape, not licence.

### OpenWeatherMap

Two distinct products, and the distinction is the whole story:

- **Free plan (no subscription):** "60 API calls/minute, 1,000,000 calls/month," including the
  Current Weather API and the **"3-hour Forecast (5 days)"**
  ([pricing](https://openweathermap.org/price)). Daily forecast for 16 days and hourly forecast
  for 4 days are listed as **paid-tier (Developer or higher)** products on that same page.
- **One Call API 3.0/4.0:** "included only in the 'One Call by Call' subscription," which
  "includes 1,000 calls/day for free" and then bills per call above that
  ([One Call 3.0](https://openweathermap.org/api/one-call-3),
  [One Call 4.0](https://openweathermap.org/api/one-call-4)). One Call 3.0 gives 8 daily
  forecast days; 4.0 gives daily data "up to 1.5 years ahead."

So the 7-day requirement lands exactly in the gap. The free-forever plan gives 5 days; 7+ days
means the metered subscription. That subscription's free allowance (1,000/day) would comfortably
cover this service's usage, but it is a **pay-as-you-call product with an overage price**, which
means introducing a billing relationship into a hobby deployment in exchange for data that
Open-Meteo hands over anonymously.

**Gap I could not close:** I could not verify from a primary page whether a payment card is
required to *activate* a One Call subscription. Neither the [pricing page](https://openweathermap.org/price),
the [full price page](https://openweathermap.org/full-price), nor the
[FAQ](https://openweathermap.org/faq) states it. The FAQ does confirm the mitigation if you do
subscribe: "When you subscribe to a Pay as you Call plan, a default daily API call limit is
automatically assigned for each subscribed product," adjustable in account settings to prevent
unexpected charges. Also from the FAQ: a new key activates "automatically, up to 2 hours after
your successful registration" — so key provisioning is not instant.

### Pirate Weather

A Dark Sky-shaped API over NOAA model output — "NOAA forecast results," specifically GFS, HRRR
and NBM ([docs](https://docs.pirateweather.net/en/latest/)). Free tier is **10,000 calls/month**
(a $2/month donation doubles it to 20,000), key obtained by registering at
`pirate-weather.apiable.io`.

Data shape is excellent for our purpose — `precipIntensity`, `precipProbability`, **`precipType`**
and an `icon` field, inherited from the Dark Sky schema. `precipType` in particular is a
first-class rain/snow/sleet discriminator, which is precisely the `{wet, snow}` split we need,
and no other candidate exposes it quite so directly.

It is the **strongest runner-up**, and worth remembering if Open-Meteo ever becomes unavailable.
It ranks second only because it needs a key, has a monthly rather than daily budget (10,000/month
≈ 333/day, still ample but an order of magnitude tighter than Open-Meteo), and is a small
volunteer-scale project — a real consideration for a dependency meant to sit untouched for years.

### WeatherAPI.com

Free plan: **100K calls/month**, **3-day forecast**, no credit card required, free forever, and
commercial use permitted ([pricing](https://www.weatherapi.com/pricing.aspx)). The generous call
budget is irrelevant — **3 days does not reach the week horizon**, so this is disqualified on
range regardless of everything else.

### Tomorrow.io

**Gap I could not close.** The [product page](https://www.tomorrow.io/weather-api/) advertises a
free plan with a "5-Day Forecast," which is already short of 7 days. The rate-limit
documentation states only that "according to your plan, you are limited to a certain amount of
requests per hour and day" and notes rate-limit *headers* are "currently available only for
Enterprise accounts" ([docs](https://docs.tomorrow.io/reference/rate-limiting)). Their support
article on free-plan limits returned **HTTP 403** to automated fetching, and web search surfaced
a figure of ~500 calls/day that I **could not confirm against a primary page** — treat it as
unverified. Given the 5-day range is disqualifying on its own, I did not pursue this further.

---

## 2. Forecast range and period granularity

The requirement is modest and worth restating before comparing: Scarcity looks only at the active
Pattern's week, so **7 days is the ceiling, not a floor to exceed**. Nobody needs 16 days.

| Provider | Max range (free) | Daily rows? | Hourly rows? | Verdict |
|---|---|---|---|---|
| **Open-Meteo** | 16 days (`forecast_days` 0–16, default 7) | **Yes** — true calendar-day rows | Yes — 168 for 7 days | Fits exactly |
| **NWS** | ~7 days | **No** — 12-hour day/night periods | Yes (separate endpoint) | Range fits, shape doesn't |
| **OWM free** | 5 days | No — 3-hour steps | 3-hourly | **Short by 2 days** |
| **OWM One Call** | 8 days | Yes | 48 hours | Fits, but metered |
| **Pirate Weather** | daily + hourly (exact max not stated in docs) | Yes | Yes | Fits |
| **WeatherAPI** | 3 days | Yes | Yes | **Short by 4 days** |
| **Tomorrow.io free** | 5 days | Yes | Yes | **Short by 2 days** |

**Open-Meteo [live probe].** A single request with `current=`, `daily=` and `hourly=` parameters
and `forecast_days=7` returned, in one response body:

```
current  {'time': '2026-08-08T16:00', 'interval': 900, 'weather_code': 0, 'precipitation': 0.0}
daily    7 rows, dates 2026-08-08 … 2026-08-14
hourly   168 rows
```

Two things follow from that one response, and both matter more than they first appear.

**First, one call serves both fetch kinds.** `CONTEXT.md` distinguishes a current-conditions check
from a forecast check as two different evaluation-time behaviours. They do not have to be two
different HTTP requests. A single Open-Meteo call can satisfy a firing decision *and* a week's
worth of ranking, which collapses the caching design (see [§4](#4-rate-limits-versus-actual-cadence)).

**Second, `interval: 900` is the API telling us its own cache TTL.** The current block declares
its values valid for 900 seconds. That is a documented answer to "how long should we cache
current conditions" rather than a number we have to invent.

**NWS [live probe].** `/points/{lat},{lon}` returned grid `ILN 85,81` plus the forecast URLs; the
`/gridpoints/ILN/85,81/forecast` response contained **14 periods** spanning
`2026-08-08T16:00-04:00` → `2026-08-15T06:00-04:00`. So the range is right — a shade over 7 days —
but the periods are **12-hour day/night halves** (`isDaytime: true|false`), named `"This
Afternoon"`, `"Tuesday Night"`. There is no calendar-day row. Producing one means merging each
day/night pair, and deciding what a Window straddling 18:00 inherits. That is real work, and it is
work Open-Meteo simply does not require.

Note also the **two-call minimum**: NWS requires resolving lat/lon → grid via `/points` before
forecasts can be fetched. That result is stable and cacheable indefinitely for a fixed home
location, so it is a one-time cost, not a per-fetch one.

---

## 3. Condition granularity

This section decides the recommendation, so it is worth being concrete about what
`{dry, wet, snow}` actually requires: a **total function** from the API's condition value to one of
three buckets, that a reader can verify by inspection and that will not silently change meaning
when the provider edits a string.

### Open-Meteo — WMO code 4677, plus numeric precipitation

Conditions are published as WMO weather interpretation codes
([docs](https://open-meteo.com/en/docs)):

| Code | Description |
|------|-------------|
| 0 | Clear sky |
| 1, 2, 3 | Mainly clear, partly cloudy, overcast |
| 45, 48 | Fog and depositing rime fog |
| 51, 53, 55 | Drizzle (light, moderate, dense) |
| 56, 57 | Freezing drizzle (light, dense) |
| 61, 63, 65 | Rain (slight, moderate, heavy) |
| 66, 67 | Freezing rain (light, heavy) |
| 71, 73, 75 | Snowfall (slight, moderate, heavy) |
| 77 | Snow grains |
| 80, 81, 82 | Rain showers (slight, moderate, violent) |
| 85, 86 | Snow showers (slight, heavy) |
| 95, 96, 99 | Thunderstorm (with/without hail) |

The bucketing is mechanical, and the ranges are contiguous by design:

- **`snow`** — 71–77, 85–86 (and arguably 56–57, 66–67 for freezing precipitation)
- **`wet`** — 51–67, 80–82, 95–99
- **`dry`** — 0–3, 45–48

That is a `switch` over integer ranges. No string comparison, no provider-specific prose, and the
vocabulary is a **WMO international standard** rather than a vendor invention — which is the real
argument, because it means the mapping is stable against Open-Meteo changing anything about its
own presentation.

Crucially, **the codes are not the only signal.** The same response carries numeric precipitation,
and this is what makes a genuine `dry` determination possible rather than a guess **[live probe]**:

```
daily.weather_code                  [3, 3, 96, 95, 95, 53, 3]
daily.precipitation_sum (mm)        [0.0, 0.0, 16.3, 77.5, 7.6, 2.3, 0.0]
daily.precipitation_probability_max [25, 7, 52, 69, 69, 57, 23]
```

Read those three rows together. Day 0 and day 1 are `code 3` (overcast) with **0.0 mm** and 25%/7%
probability — unambiguously `dry` for the purpose of "can I do this outside," even though the sky
is grey. Day 5 is `code 53` (moderate drizzle) with 2.3 mm and 57% — `wet`. The codes alone would
have called both days by their sky; the numbers separate *overcast* from *raining*, which is
exactly the distinction a Task tagged `dry` cares about.

This also means the `dry` threshold is a **tunable domain decision** — a mm cutoff, a probability
cutoff, or both — rather than a property we inherit from the provider. `precipitation_probability`
is documented as "probability of precipitation with more than 0.1 mm of the preceding hour"
([docs](https://open-meteo.com/en/docs)), so its semantics are precise enough to threshold
against. `snowfall_sum` is separately available for the snow split.

### NWS — prose, and the reason it loses

The `/forecast` endpoint's condition field is `shortForecast`, a free-text string. Here is the
complete set of distinct values across all 14 periods of one real response **[live probe]**:

```
Sunny
Chance Showers And Thunderstorms
Showers And Thunderstorms
Showers And Thunderstorms Likely
Chance Rain Showers then Chance Showers And Thunderstorms
Chance Showers And Thunderstorms then Partly Cloudy
Partly Sunny then Showers And Thunderstorms Likely
Showers And Thunderstorms Likely then Chance Showers And Thunderstorms
```

Four of those eight are **compound phrases joined by `"then"`** — two conditions in one period,
in one string, with no delimiter contract. Bucketing this means substring-matching English, and
then deciding which half of a `"then"` a Window falls in. `detailedForecast` is worse, being a
full paragraph ("A chance of showers and thunderstorms. Partly sunny, with a high near 88…").
`probabilityOfPrecipitation` *is* structured (`{unitCode: "wmoUnit:percent", value: 36}`), which
would support a probability-threshold `dry` rule — but there is no precipitation *amount* and no
rain/snow discriminator at this endpoint, so `{wet}` versus `{snow}` cannot be told apart from
`/forecast` at all.

**There is a better NWS path, and it is still worse than Open-Meteo.** The raw
`/gridpoints/{office}/{x},{y}` endpoint (no `/forecast` suffix) exposes ~57 structured layers
including `quantitativePrecipitation` (mm), `snowfallAmount` (mm), `probabilityOfPrecipitation`
(%), and a `weather` layer with genuine enums **[live probe]**:

```json
{"coverage": "patchy", "weather": "fog", "intensity": null,
 "visibility": {...}, "attributes": []}
```

So the data exists. The cost is the encoding: every layer is a list of
`{validTime, value}` pairs where `validTime` is an ISO 8601 **interval with a varying duration** —
observed `PT6H`, `PT5H`, `PT2H` and `PT1H` **within a single response**. Consuming it means
expanding ragged, non-aligned intervals per layer and re-aligning them against each other and
against Window boundaries, then still hand-rolling day aggregation from [§2](#2-forecast-range-and-period-granularity).

That is a meaningful chunk of parsing code, carrying its own bugs, to reach a dataset Open-Meteo
returns pre-aligned on clean hourly and daily grids. **For a US-only provider, that is the entire
case against NWS** — not licence, not cost, not range, but the shape of the payload.

### OpenWeatherMap

Condition codes are grouped by a `main` field — Thunderstorm (2xx), Drizzle (3xx), Rain (5xx),
Snow (6xx), Atmosphere (7xx), Clear (800), Clouds (80x)
([conditions](https://openweathermap.org/weather-conditions)). That top-level grouping buckets
cleanly — `Rain`/`Drizzle`/`Thunderstorm` → `wet`, `Snow` → `snow`, `Clear`/`Clouds` → `dry` —
and the 5-day endpoint provides `pop` (0–1) plus `rain.3h`/`snow.3h` volumes in mm
([forecast5](https://openweathermap.org/forecast5)), which is enough for a thresholded `dry`.
The docs note "it is possible to meet more than one weather condition," with the first treated as
primary. **Condition granularity is not OWM's problem — range and metering are.**

### Pirate Weather

`precipType` is the cleanest single field of any candidate for the `wet`/`snow` split, alongside
`precipIntensity` and `precipProbability` ([docs](https://docs.pirateweather.net/en/latest/)).
Bucketing is near-trivial. This is what keeps it as the runner-up.

### Does the domain need more than three buckets?

The issue asks whether `{dry, wet, snow}` suffices or whether temperature bands and wind are
needed. **This research does not answer that** — it is a domain question for `CONTEXT.md`, not a
provider question. What it does establish is that the choice **does not constrain the provider**:
Open-Meteo returns temperature, wind speed, wind gusts, humidity, cloud cover and visibility on
the same hourly and daily grids, in the same call, at no extra cost. Whatever the Weather Tag
vocabulary settles on, the data is already in the response.

That is worth stating plainly because it removes a dependency: **the Tag vocabulary can be decided
later, independently, without reopening this ticket.**

---

## 4. Rate limits versus actual cadence

The issue treats rate limits as an open risk. They are not, and the arithmetic is worth writing
down so the question stays closed.

**Ceiling on demand.** Under the lazy rule, calls happen only when an Active Task carries a
Weather Tag. Even assuming that is *always* true and the engine ticks every 15 minutes around the
clock — the pessimistic bound, not the expected case — that is **96 calls/day**. Against
Open-Meteo's 10,000/day, 5,000/hour and 600/minute
([terms](https://open-meteo.com/en/terms)), the service uses **under 1%** of the daily allowance.
The per-minute limit of 600 is not reachable by a system that ticks four times an hour.

For one household with one location, on every candidate that survived [§1](#1-candidates-and-free-tier-terms),
the free tier is not remotely a concern. **Choose on data shape and licence; the limits do not
discriminate between these providers.**

**Sensible caching, given that.** Since limits aren't binding, cache for latency, resilience and
politeness — not for compliance:

| Cache | TTL | Why |
|---|---|---|
| Current conditions | **15 min** | Matches the API's own `interval: 900` **[live probe]** — refreshing faster returns identical values. |
| Week forecast | **1–3 hours** | Models update on the order of hours; a daily refresh would go stale for same-day ranking, and the call budget makes hourly free. |
| NWS `/points` → grid | **indefinite** | Pure function of a fixed home coordinate. Only relevant if NWS is chosen. |

**One cache, not two.** Because a single Open-Meteo call returns `current` + `daily` + `hourly`
together **[live probe]**, the right structure is **one cached response** refreshed on the shorter
(15-minute) TTL, with both the current-conditions check and the forecast check reading from it.
That collapses the two fetch kinds in `CONTEXT.md` into one fetch *mechanism* — the domain
distinction stays, but it becomes two readers of one cached payload rather than two network paths
with separate failure modes. **Fewer failure modes is the real win here, not fewer calls.**

**A serving suggestion, not a requirement:** because the whole week arrives in every response, the
cache can be a single in-memory object holding the last successful payload plus its timestamp. On
fetch failure the settled fail-closed behaviour applies — but a recent-enough cached payload is a
strictly better answer than failing closed, and it costs nothing to prefer it. Whether a stale
payload should satisfy a check, and for how long, is a domain decision this document deliberately
leaves open.

---

## 5. Practical fit — Pi 5 / .NET 10 / ARM64 Docker Swarm

**No provider-side blockers for Open-Meteo.** Specifically:

- **No API key** ⟹ no secret to provision into the Swarm service, no Docker secret, no key
  rotation, no `dcm secrets sync` step. For a deployment meant to run unattended for long
  stretches, **the credential that doesn't exist is the one that can't expire.**
- **No callback registration**, no HTTPS webhook, no inbound anything. Purely outbound HTTPS from
  the Pi. This matters given the Tailscale-only posture: the service never needs public
  reachability, and nothing about the weather integration pressures that.
- **No geographic restriction** — global coverage, unlike NWS's US-only scope.
- **Architecture-independent** — a plain HTTPS + JSON call. ARM64 is a non-question.

**Note the asymmetry with NWS on identification.** NWS requires a `User-Agent` identifying the
application ([docs](https://www.weather.gov/documentation/services-web-api)) — not a secret, but a
real header that must be set correctly or requests may be rejected. Open-Meteo requires nothing.
Either way, **set a descriptive `User-Agent`**; it is good citizenship on an anonymous free tier
and it is what lets a provider contact you rather than block you.

### .NET client libraries

**There is no official Open-Meteo .NET SDK.** Open-Meteo's own SDK list does not include .NET, and
NuGet carries only community packages, none with the download volume or backing that would justify
a dependency:

| Package | Version | Downloads |
|---|---|---|
| `OpenMeteo.dotnet` | 2.0.0 | ~26,800 |
| `OpenMeteo.dotnet.client.sdk` | 5.10.1 | ~12,700 |
| `OpenMeteoApi` | 1.3.0 | ~1,600 |
| `OpenMeteo` (FlatBuffers decoder) | 1.0.1 | ~1,654 |

(NuGet search API, retrieved 2026-08-08.)

**Recommendation: use `HttpClient` + `System.Text.Json` directly.** This is not a grudging
fallback. The integration is one GET with query parameters and one response record — the JSON in
[§2](#2-forecast-range-and-period-granularity) is the whole surface. A third-party wrapper around
that adds a maintenance dependency, an upgrade obligation and an abstraction between us and a
payload we would rather read literally, in exchange for saving perhaps thirty lines. `HttpClient`
with `IHttpClientFactory` and a typed client is the .NET-idiomatic shape and is entirely
sufficient here.

Open-Meteo also offers a **FlatBuffers** response encoding for high-volume consumers. At ~96
calls/day it is irrelevant — **use JSON**, which is human-readable in logs and debuggable by eye.

---

## 6. The attribution obligation

Open-Meteo's data is CC-BY 4.0 and "attribution is a requirement under this licence"
([pricing](https://open-meteo.com/en/pricing)). This is the one obligation the recommendation
carries, and it should be recorded rather than discovered later.

It is genuinely small. **The domain already has the surface**: `CONTEXT.md` establishes a footer
that names failed fetched-Dimension checks ("Weather unavailable"). A persistent
"Weather data by [Open-Meteo.com](https://open-meteo.com/) (CC BY 4.0)" line in that same footer
discharges the licence, and it sits naturally beside the failure note — same place, same subject.

Worth noting for completeness: this is a private, single-user, Tailscale-only service, so the
"public" that attribution serves is one person. The obligation is still worth honouring simply
because it is free to honour.

**By contrast, NWS carries no attribution obligation at all** — US Government open data, "free to
use for any purpose" ([docs](https://www.weather.gov/documentation/services-web-api)). If the
attribution line were ever unwelcome, that is the trade being made.

---

## 7. What this means for the ticket

1. **Provider: Open-Meteo.** No key, global, 10,000 calls/day free, and "personal home
   automation" is a named example of qualifying non-commercial use.
2. **Range: `forecast_days=7`.** Matches the Pattern week exactly. Nothing longer is needed;
   Open-Meteo's 16-day ceiling is headroom we will not use.
3. **Granularity: WMO code + numeric precipitation, bucketed to `{dry, wet, snow}` in our code.**
   Request `weather_code`, `precipitation`, `precipitation_probability` and `snowfall_sum` on both
   the hourly and daily grids. The exact `dry` threshold (mm, probability, or both) is a domain
   decision left open — see below.
4. **Cadence: one call, one cache, 15-minute TTL.** Both the current-conditions check and the
   forecast check read the same cached payload. Rate limits are a non-issue at ~1% of allowance.
5. **Client: `HttpClient` + `System.Text.Json`, JSON not FlatBuffers, descriptive `User-Agent`.**
   No third-party SDK.
6. **Attribution: one footer line**, alongside the existing fetched-Dimension failure note.

**Fallback, if Open-Meteo ever becomes unavailable or changes terms: Pirate Weather.** It needs a
key and has a tighter monthly budget, but its `precipType` field makes the `{wet, snow}` split
cleaner than anything else surveyed. Keeping the provider behind a narrow internal interface —
"give me the bucket for this instant, and for each day this week" — makes that swap cheap, and is
worth doing for that reason alone.

### Questions this research deliberately leaves to the domain

These are `CONTEXT.md` decisions, not provider decisions, and none of them constrain the choice
above:

- **What counts as `dry`?** A mm threshold, a probability threshold, or both — and does an
  overcast, 0.0 mm day qualify? (Per [§3](#3-condition-granularity), the data supports any of
  these; the question is what the user means by the Tag.)
- **Does the Tag vocabulary stay at three buckets**, or extend to temperature bands and wind?
  Open-Meteo returns all of it in the same call either way.
- **May a stale-but-recent cached payload satisfy a check** instead of failing closed, and for how
  long? (See [§4](#4-rate-limits-versus-actual-cadence).)
- **How does a Window straddling a day boundary** read a *daily* forecast row — or does forecast
  evaluation use the hourly grid throughout, with daily rows only for coarse ranking?

---

## Sources

Primary and first-party only, plus live API responses captured directly. Nothing below rests on
third-party write-ups.

**Open-Meteo**
- [Terms of Service](https://open-meteo.com/en/terms) — call limits, non-commercial definition,
  fair-use and blocking clause
- [Pricing](https://open-meteo.com/en/pricing) — no-key free tier, per-interval limits, CC-BY 4.0
  attribution requirement
- [Weather Forecast API docs](https://open-meteo.com/en/docs) — variables, `forecast_days` range,
  WMO weather code interpretation table, precipitation-probability definition
- **[live probe]** `GET https://api.open-meteo.com/v1/forecast?latitude=39.9612&longitude=-82.9988&current=weather_code,precipitation&daily=weather_code,precipitation_sum,precipitation_probability_max,snowfall_sum&hourly=weather_code,precipitation,precipitation_probability&timezone=America%2FNew_York&forecast_days=7`
  — 2026-08-08, unauthenticated, `HTTP/1.1 200 OK`

**US National Weather Service**
- [Weather.gov API documentation](https://www.weather.gov/documentation/services-web-api) —
  User-Agent requirement, rate-limit language, open-data terms, endpoint list
- [OpenAPI specification](https://api.weather.gov/openapi.json) — `GridpointForecast` period schema
- **[live probe]** `GET /points/39.9612,-82.9988`, `GET /gridpoints/ILN/85,81/forecast`,
  `GET /gridpoints/ILN/85,81` — 2026-08-08, with identifying User-Agent

**OpenWeatherMap**
- [Pricing](https://openweathermap.org/price) — free-tier limits and included products
- [Full price list](https://openweathermap.org/full-price)
- [One Call API 3.0](https://openweathermap.org/api/one-call-3) ·
  [One Call API 4.0](https://openweathermap.org/api/one-call-4) — subscription requirement,
  1,000 calls/day allowance, forecast ranges
- [5 day / 3 hour forecast](https://openweathermap.org/forecast5) — range, `pop`, `rain.3h`,
  `snow.3h`
- [Weather condition codes](https://openweathermap.org/weather-conditions) — `main` groups
- [FAQ](https://openweathermap.org/faq) — pay-as-you-call daily limit setting, 2-hour key
  activation

**Pirate Weather**
- [Documentation](https://docs.pirateweather.net/en/latest/) — registration, free tier,
  NOAA sources, Dark Sky-compatible fields

**Others**
- [WeatherAPI.com pricing](https://www.weatherapi.com/pricing.aspx)
- [Tomorrow.io Weather API](https://www.tomorrow.io/weather-api/) ·
  [Rate limiting docs](https://docs.tomorrow.io/reference/rate-limiting)
- NuGet search API (`azuresearch-usnc.nuget.org`), query `open-meteo`, retrieved 2026-08-08

**Sources that did not pan out.** Tomorrow.io's support article on free-plan rate limits
(`support.tomorrow.io/hc/en-us/articles/20273728362644`) returns **HTTP 403** to automated
fetching; a ~500 calls/day figure appeared in web search results but is **not cited here**
because I could not confirm it first-party. Tomorrow.io's own rate-limit reference page states
limits exist without publishing numbers. NWS publishes **no numeric rate limit** anywhere I could
find — "reasonable rate limits" is the entire published statement. Neither OpenWeatherMap's
pricing, full-price, nor FAQ pages state whether a payment card is required to activate a One Call
subscription.

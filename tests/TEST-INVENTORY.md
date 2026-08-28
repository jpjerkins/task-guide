# Failing-test inventory

The test list, not the tests. Every line below is red today because nothing is implemented; a line
is green when the behaviour it names holds. Written from `CONTEXT.md` — where a line paraphrases,
`CONTEXT.md` wins.

**How to use it.** The **fan-out** sections are pure functions with no shared state, no I/O, and a
list writable before the implementation — this is where a fleet of agents pays. The **sequential**
sections share the write lock and the read view, and belong to one session working against the
walking skeleton (#51).

---

## Fan-out · `TaskGuide.Domain.Tests`

### Status — derived, ordered, first-match-wins

- a Task with a completion entry covering the current instance reads `Done`
- a Task with no Duration reads `Unprocessed`
- **a Task with no Duration that is also 59 days old reads `Unprocessed`, not `Stale`** — the order
  is the rule, and this is the case that proves it
- a Task with no Duration that is also completed reads `Done` — completion outranks everything
- an undeadlined one-off Task aged past the threshold reads `Stale`
- **a one-off Task with a Deadline is never staled by age**, however old
- a recurring Task with N consecutive missed instances reads `Stale`
- a recurring Task with N-1 consecutive misses and one completion between them reads `Active`
- a Task past its Deadline reads `Active` — overdue is not a state
- nothing in the model can write a Status; the type exposes no setter and storage carries no field
- a Task with non-null `Provenance` is never `Unprocessed` and never `Stale` — a derived Task was
  neither captured nor neglected
- a Task with non-null `Provenance` cannot be postponed; `CanPostpone` is a pure query, so the
  rule is readable without reaching into the Task's lifecycle

### Eligibility and the two clocks

- `eligible = Active AND now >= Defer AND now >= Postpone`, every term computed on read
- a deferred Task is absent from every match-driven surface but present in the task list
- `age = now - max(CreatedAt, Defer)` — Defer pauses the age clock
- **Postpone does not pause the age clock**; a Task pushed away repeatedly still stales on schedule
- a postponed Task cannot also be deferred-in-the-future — the gesture only reaches Tasks whose
  Defer has elapsed, so `max` needs no third term
- an offset Defer on a recurring Task resolves against the generated Deadline, per instance
- a recurring Task rejects an absolute Defer

### Offset

- `N days/weeks/months before` resolves against its anchor
- `the last Friday strictly before` a Friday anchor resolves to the **previous** week
- `the last Friday strictly before` a Saturday anchor resolves to the day before
- a month-unit offset from the 31st lands on a real date

### Recurrence

- calendar rules generate the next deadline from the calendar, ignoring completions
- completion rules generate it from `last(completed) + interval`
- **a completion-anchored Task never accrues a backlog** — never done, no new instance
- a completion-anchored Task with no completions uses `firstDue`, else `CreatedAt`
- exactly one instance is live at a time
- **grace equals one full recurrence period** — an instance stays completable until the next
  instance's deadline arrives, derived, with no knob
- a late completion satisfies the instance that was live and logs `{ due, done }`
- a missed instance is silently superseded — no second live item, no `Stale`
- monthly-on-the-5th stays on the 5th across a year
- a one-off Task's log holds at most one entry, and that entry is what makes it `Done`
- **a rule that never advances is rejected at construction** — an interval below 1 makes the
  live-instance walk non-terminating on the tick thread, so it is refused, not survived
- a weekly rule naming no weekday, and a calendar date the rule can never fall on, are rejected
  at construction
- an anchor paired with a rule it cannot run is rejected at construction, named rather than
  discovered as a cast failure inside the generator

### Matching — the two algebras

Categorical, from `CONTEXT.md`'s table, one test each:

| Task | Window | Fits |
|---|---|---|
| `{}` | `{}` | yes |
| `{}` | `{garage}` | yes |
| `{garage}` | `{}` | **no** |
| `{garage}` | `{garage, outside}` | yes |
| `{Sam, Ana}` | `{Sam}` | **no** |
| `{Sam, Ana}` | `{Sam, Ana, the kids}` | yes |

- ordinal: task value ≤ window value fits
- ordinal: a task value above the window's ceiling does not fit
- an ordinal axis silent on the Task side takes the task-side default
- an ordinal axis silent on the Window side takes the window-side default
- **a categorical axis has no default on either side** — absence is ∅, and a Window declaring
  nothing admits only untagged Tasks
- matching is a conjunction across axes: failing one axis fails the Task
- a rule reads only its own axis
- **loose Tags are ignored by matching** on both sides
- a mistyped Tag (`#garge`) admits the Task to *more* Windows, not fewer
- a fetched axis (Weather) reads its Window-side set from the fetched values, not the Window's
  own authored Tags; unfetched/unknown resolves to ∅ and fails closed, the same as absence
  anywhere else on a categorical axis

### Duration as a derived ceiling

- a 45-minute Window admits the 30 bucket and below, and not 60
- a Window's ceiling is derived from its length and cannot be authored
- a 60-minute Window admits the 60 bucket exactly (boundary)
- 60 minutes snaps to the 60 bucket, not `longer` (boundary)
- both directions derive their bucket minutes from `KnownDimensions.DurationBuckets`, not a
  private copy — one test per declared sized bucket (2/10/30/60)
- raw minutes from a capture path snap **up** to the next bucket (45 → 60)
- 61 minutes snaps to `Longer`
- a Window **longer** than the largest sized bucket derives the unsized bucket — 90 minutes and
  four hours both derive `longer`, and so does one minute past the boundary on both directions
- a Window exactly at the largest sized bucket still derives that bucket, not the unsized one
  (the boundary must not drift)
- a `longer` Task fits a long Window and still fails one at the largest sized bucket

### Dimension registry

- **a registry declaring one value on two Dimensions refuses to start**, naming the value
- a duplicate is rejected at startup, not resolved at the point of use
- identity and label are independent: renaming the label touches no stored Tag
- a categorical Dimension derives a multi-select control; an ordinal one derives a slider
- an ordinal Dimension declaring a default derives a "leave at the default" control; **Duration,
  declaring none, derives no such control**

### The promote/demote sweep

- declaring a Dimension value matching a loose Tag moves it into that Dimension's slot on every
  Task and Window carrying it
- withdrawing a value returns those Tags to the loose bag **with their strings intact**
- promote-then-demote is lossless — the round trip is identity
- an **ordinal** axis takes up a loose Tag only if the record has no value on that axis
- a deliberately-set ordinal value is never overruled by a loose Tag

### Ranking

- the sort is total: no two Tasks compare equal unless every key ties
- band 1 (passed) outranks band 2 (within horizon) outranks band 3 (no pressure)
- **within a band, fewest Opportunities first**
- on an Opportunities tie, longest Duration first
- on a Duration tie, oldest `CreatedAt` first
- a Task with a Deadline does not automatically outrank one without — bands, not a continuous key
- inside band 2, a sooner Deadline yields a shorter horizon and therefore a higher rank, without a
  deadline key being applied on top

### Opportunities and the horizon

- without a Deadline the horizon is a true rolling 7 × 24h
- **a once-a-week opportunity counts exactly once at any hour outside it** — and twice while you
  are standing in it, when the one you are in and next week's both count
- with a Deadline ahead the horizon runs to the end of that day
- **with a Deadline passed the bound is dropped** and the horizon reverts to a rolling 7 days
- an overdue Task therefore never misreports as an Orphan
- the count walks real dates, so an Override removing the only admitting Window drops it
- a dated Event displacing a Window drops it
- switching the active Pattern moves the count
- the Pattern-week count ignores Overrides and Events
- the Pattern-week count is defined for a Task that is not currently eligible
- **a Window you are standing in still counts as an Opportunity** — the near edge reads the
  Window's end, not its start
- the far edge of the horizon is unchanged: a Window starting at the horizon end never counts
- a fetched axis constrains nothing in the Pattern-week count, so a weather-tagged Task is not an
  Orphan
- `CountAhead` still fails closed on a fetched axis it cannot know for a future Window

### Orphan detection

- `Opportunities = 0` **and** Pattern-week count 0 → Orphan
- `Opportunities = 0` with a non-zero Pattern-week count → "none in this stretch", **not** an Orphan
- an `Unprocessed` Task is never an Orphan — orphan-ness is undefined without a Duration
- a `Stale` Task is never an Orphan
- **a deferred Task can be an Orphan** — orphan detection ignores the clock gates
- a postponed Task can be an Orphan, for the same reason
- a derived Task is subject to orphan detection
- an Event is never subject to it
- Orphan is never counted in the process/stale footer counts — the three are disjoint
- `Opportunities = 1` gets no badge
- a fetched axis never makes a zero read as an Orphan — the Pattern-week count is counterfactual

### Day boundary and clock-time resolution

- the day boundary is local midnight in `America/Chicago`, everywhere
- an ambiguous start on the fall-back day resolves to the **first** occurrence
- a nonexistent start in the spring gap **clamps to the gap's end**
- a span crossing a transition is honestly an hour shorter or longer
- a span crossing the spring transition is honestly an hour shorter
- a span crossing the fall-back transition is honestly an hour longer
- **a Window lying entirely inside the spring gap has zero length and does not fire**
- Deadline, Defer and Postpone resolve at the day boundary

### Snooze arithmetic

- interval is `clamp(25% of length, 5 min, 30 min)`
- a 10-minute Window floors at 5 minutes; a 4-hour Window caps at 30
- `offered ⟺ now + interval < the Reminder's Day boundary`
- a Window firing at 11:50p offers no Snooze
- **a Reminder that fired at 10:30p and is tapped at 12:05a offers no Snooze** — the boundary is
  the Reminder's own
- the ceiling re-derives from the time remaining at each re-fire
- past the Window's end the ceiling **floors at the smallest bucket**, not at whatever was last
  derived — the rule is stateless
- every other Dimension value stays frozen at the original Window's
- an empty re-fire pushes once and ends the chain

### Derived-obligation rules

- a rule reads a dated record and produces a read-only Task carrying provenance
- **Tags are never inherited from the trigger**
- moving the trigger's date moves the obligation's Deadline
- deleting the trigger deletes the obligation, with no cleanup pass
- removing the triggering Tag deletes the obligation
- a completion keyed `{ ruleId, triggerId, due }` stops matching when the trigger moves, so the
  obligation re-derives
- past its own Deadline the obligation stays live at maximum urgency
- past the **trigger's** date it silently stops being derived — no `Stale`, no count
- a recurring Event never triggers
- **absence, not overlap**: an Event merely overlapping the commitment derives nothing
- an Override stamped without the commitment derives it
- a deleted instance (Event exception) derives it
- **a moved instance does not** — the case a delete-only tombstone would have broken
- three contiguous absences derive **one** obligation, due before the first
- a derived Task is never `Unprocessed` and never `Stale`
- a derived Task cannot be postponed
- a moved instance on an **Overridden** date still does not derive — the moved case driven through
  the absence check rather than around it
- a renamed instance the shape still carries derives nothing
- **a coalesced run whose first absence has passed survives while a later one remains** — the
  run's last date is what says the obligation has expired; the Deadline stays anchored to the first
- a run wholly in the past still stops being derived, as does a lone absence the day after it
- the context takes an instant and derives today from its own boundary — supplied, never reached
  for, so nothing in it can disagree about what day it is

---

## Sequential · `TaskGuide.Application.Tests`

### The tick loop

- a Window fires at its start
- **a Window that matches nothing sends nothing** — the biconditional, and the only restraint lever
- a Window is not re-fired part-way through, however long it is
- there is no daily cap: N authored Windows all matching fire N times
- a Window whose start passed while the service was down **fires late inside its own span**, with
  the ceiling re-derived from `now → end`
- a Window whose span closed while the service was down **is silent**
- a long outage produces **no burst on return**
- a pending Snooze survives a restart, because it is just a row
- the retention sweep runs unguarded on every tick
- `unfired?` is answered by the Fire record, so a slow tick never double-fires

### Events, carriers and the fallback push

- a dated Event appears in the footer for the 3 days up to and including its day
- during the runway, **the day's first Window that actually fires** fires unconditionally
- a Window eaten by downtime does not consume the carrier duty — it slides to the next chance
- later Windows in the runway keep the normal rule
- a **recurring** Event surfaces on its own day only, with no unconditional firing
- a windowless runway day fires the **fallback push at exactly 11:00a**
- a runway day whose Windows were all eaten fires it **when the last span closes**
- the fallback push never fires after the day boundary
- it fires late on return, up to the day boundary
- it names no Window
- **it offers no Snooze at all** — not a disabled one
- a non-runway windowless day fires nothing

### Delivery

- `firedAt` is written **only when Pushover accepts**
- a rejected push reads as unfired next tick and is retried
- retries stop when the span closes (opportunity) or at the boundary (obligation)
- every failed attempt is logged
- a Receipt is **never** retried
- a Receipt is not written to the Fire record

### Notification content

- the title is the top-ranked Task in full, with its Duration
- the shortlist is three, then `+N more` when N ≥ 1
- Events sit above the Tasks, date ascending
- zero-valued footer components are omitted; all-zero drops the line
- a failed fetched-Dimension check is named in the footer when UI-visible
- **the same failure is silent when headless**
- an unconditional fire with no matches leads with the **Event**, not the Window
- `ttl` runs to the Window's end for a window fire and a late one
- `ttl` runs to the day boundary for a snooze past the span, an unconditional fire and a fallback
- a Receipt's `ttl` is 24 hours **from sending**

### Weather, the fetched axis

- no weather-tagged Active Task ⇒ **no API call**
- a firing uses current conditions; a future evaluation uses the forecast
- unknown weather matches nothing (fails closed) in both headless and UI-visible cases
- only the UI-visible case adds the footer note

### Capture and Receipt

- capture with a Duration produces an `Active` Task
- capture without one produces an `Unprocessed` Task
- raw minutes snap up
- **no capture path writes Tags**
- capture from any of the three Shortcuts sends a Receipt
- **in-app capture sends none**
- a capture that cannot reach the server fails loudly and queues nothing

### Glance

- recomputed every tick, sent only when the payload differs from the last one **sent**
- not sent again inside 30 minutes
- **a Window start preempts the floor**
- **a Window end does not**
- between Windows it shows the next Window's start, weekday included when not today
- inside a Window with no match it shows the same fall-through shape
- it is never blank
- one retry at the next tick, ignoring the floor; never two

### Liveness

- `/health` reports `{ ok, lastTick, storage, uptime }`
- a stalled loop reports `ok: false` while HTTP still answers
- **read health parses the file** — a truncated or empty file reports unreadable where `stat` would
  have passed
- write health is read off the retention sweep's outcome, not a probe
- a registry collision signals outbound before exiting
- load, memory and Pushover reachability appear nowhere in the predicate

---

## Sequential · `TaskGuide.Storage.Tests`

Against `fixtures/data`, the golden store.

- the whole store loads into typed objects at startup
- every read is served from memory
- **a mutation writes the affected file(s) before the request returns**
- a write is atomic: a killed process leaves the old file or the new one, never a partial
- one global write lock serialises mutations
- **a read never blocks on a write**
- `tasks.json` round-trips with no `status` field
- an unknown field written by a newer binary **survives a load-and-save round trip**
- `day-templates.json` round-trips the golden store unchanged
- a Window's start and end round-trip as authored clock times, never as instants
- an Event prototype's `absenceNotice` Offset round-trips, and a null one stays null
- an unknown field on a Day template survives a load-and-save round trip
- no codec writes a `status` property, whatever type it would carry — `TaskCodec`
- no codec writes a `status` property, whatever type it would carry — `DayTemplateCodec`
- `patterns.json` round-trips the golden store unchanged
- a Pattern's seven days are indexed by weekday with Sunday first
- a Pattern book whose `days` array is not seven long is rejected at read, naming the Pattern
- no codec writes a `status` property, whatever type it would carry — `PatternCodec`
- `overrides.json` round-trips the golden store unchanged
- a one-off day round-trips with a null `used`
- an unknown field on an override survives a load-and-save round trip
- no codec writes a `status` property, whatever type it would carry — `OverrideCodec`
- `events.json` round-trips the golden store unchanged
- an Event's loose Tags survive the round trip, and are what a derived-obligation rule reads
- `event-exceptions.json` round-trips both the delete row and the edit row
- an Event exception that is neither a delete nor an edit is rejected at read, naming its date
- an Event's `absenceNotice` round-trips, and a null one stays null
- no codec writes a `status` property, whatever type it would carry — `EventCodec`
- `manifest.json` version mismatch runs the ordered N→N+1 steps at startup
- a snapshot is written once per startup, and **only when that startup will write**
- snapshots keep the last 5
- an Event-plus-Override write puts the **Event first**
- a crash between the two leaves the state the overlap check detects, and the next read re-offers
  the prompt
- an Override's copy **preserves each Window's id**
- a date materialised mid-day does not re-fire an already-fired Window
- an Override carries its `used` record with the template **name as it was**
- the use record survives the date becoming a one-off day
- re-stamping replaces the use record rather than appending
- promoting a one-off day writes the source date's use record and **does not re-link**
- `Unused` is false for a template referenced only by a **dormant** Pattern
- `Unused` is false for a template stamped within ±13 months, in **either** direction
- deleting an `Unused` template corrupts no record
- fires older than 30 days are unlinked as whole files
- a fire row carries the Window's name and span **as they were**
- `(date, null, "fallback")` is unique per day
- a completion log is not rewritten when its Task's title changes
- each completion log round-trips the golden store unchanged
- a one-off Task's entry round-trips a null `due`
- `completions/derived.json` round-trips, keyed on `ruleId` + `triggerId` + `due`
- the Task id comes from the filename, so a log file carries no id of its own
- no codec writes a `status` property, whatever type it would carry — `CompletionCodec`
- `fires/2026-08-15.json` round-trips the golden store unchanged
- `dueAt` and `firedAt` round-trip as instants while `windowStart` and `windowEnd` round-trip as clock times, in the same file
- a pending Snooze row round-trips with a null `firedAt` and reads `IsPendingSnooze`
- Fire dates are read from fire file names without parsing contents
- no codec writes a `status` property, whatever type it would carry — `FireCodec`
- a restore under a running service is invisible, and the next mutation destroys it *(the one test
  that documents a failure mode rather than preventing it — see #49's restore drill)*
- every minted id carries its type's prefix and 26 Crockford Base32 characters
- ids minted in sequence sort lexicographically in mint order
- two ids minted in the same millisecond still differ
- a minted id is accepted by its own `IPrefixedId` record struct round-trip

---

## `TaskGuide.Api.Tests`

- every endpoint in `Endpoints/` appears in the OpenAPI document
- the OpenAPI document carries a `TaskResponse` schema with its four members — the SPA's
  `Task` type is generated from it, so a bare `200: OK` is a broken contract, not a cosmetic gap
- `GET /api/tasks` documents its 200 as an **array of** `TaskResponse`
- `POST /api/tasks` documents 201 (with a `TaskResponse` body), 400 and 503
- `TaskResponse.duration` is documented as a **nullable** integer
- `POST /api/reminders/{date}/{windowId}/snooze` **rejects** a re-fire crossing the day boundary,
  with the same line the disabled control shows
- `PUT /api/right-now/matching-on` writes through to that date's Override and does not stack
- `PUT /api/right-now/matching-on` is refused on a landing page past its Reminder's day boundary
- marking off is accepted on that same stale page
- `POST /api/tasks/{id}/completions` is refused on an `Unprocessed` Task
- `POST /api/tasks/{id}/postpone` is refused on a recurring Task and on a derived Task
- `GET /api/days/{date}` **writes nothing** — reading a shape never materialises an Override
- `POST /api/overrides` over a range writes one Override per date
- `GET /api/overrides/clobber-check` names every date in the range that already has one
- `DELETE /api/patterns/{id}` is refused for the active Pattern
- `GET /api/patterns/active/switch-impact` returns the orphan count **before** the switch
- `/health` is reachable without traversing `/api`

## `TaskGuide.Web` (vitest)

The SPA's `Task` type is generated from the OpenAPI document (`npm run gen:api`); nothing about
a Task's shape is written by hand. `src/api/client.ts` is the normalisation boundary.

- a string `duration` off the wire is coerced to a **number** — the generator describes an int32
  as `integer | string`, and only a value assertion catches this: `${x}m` renders `30` and `'30'`
  identically, so no component test can tell them apart
- a null `duration` stays null rather than becoming `0`
- a Task with a null `duration` renders its title and **no duration pill**
- a non-OK GET and a rejected fetch both land on the error state
- the quick-add duration chip IS the submit, and is inert while the title is empty

## `TaskGuide.E2E`

- capture a Task in the SPA, see it in the list, mark it off
- open a Reminder landing page, snooze it, see the re-fire
- **the date picker survives its own input events** on every date-entry surface — Deadline, Defer's
  absolute form, Postpone's escape, Recurrence's first-due, an Event's date, and the Override
  rail's "Pick a date…"
- a `<select>` and an ordinal slider survive the same way
- authoring an Override over a range from the rail's escape writes the whole span

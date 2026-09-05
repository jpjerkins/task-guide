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

### Tag equality

- `TagSet` equality (#115): two separately-constructed, structurally identical tag-bearing sets
  compare equal, and hash equal — the reference-equality trap a positional record falls into on
  a dictionary and a list member, the same #69/ADR-0011 watch-budget bug arriving through
  `TagSet` instead of `GlanceState`
- two separately-constructed empty sets compare equal, and equal `TagSet.Empty`
- a Dimension's values compare equal regardless of order, and hash equal
- loose Tags compare equal regardless of order, and hash equal
- a Dimension mapped to an empty list equals that Dimension being absent, and hashes equal
- a genuine difference still compares unequal: a different value on a Dimension; an extra
  Dimension; a different loose Tag; a duplicate value or loose Tag against the same one held once
- Dimension key insertion order does not change equality or the hash — `HashCode.Add` folds
  sequentially, so the per-Dimension hash contributions must be combined order-free too, not just
  the per-Dimension `Equals`
- swapping values between two Dimensions compares unequal and hashes differently — a regression
  guard against summing each Dimension's id hash and its values hash independently (additively
  decomposable), not a general promise that unequal `TagSet`s always hash differently

### Record equality over collections (#114)

The #115 trap, swept across every remaining Domain record with an `IReadOnlyList` or
`IReadOnlyDictionary` member: a positional record's synthesised `Equals` compares such a member by
**reference**. Preventive — none of these records is compared anywhere on `main` today. Each member's
semantics is decided from `CONTEXT.md`, not from convenience, and every order-insensitive `Equals` is
paired with an order-free hash (`HashCode.Add` folds sequentially, so an order-sensitive hash beside
an order-insensitive `Equals` breaks the contract and loses the record from a `Dictionary`).

**Sequence members** (order *is* the meaning): `Pattern.Days`, `OrdinalDimension.OrderedValues`,
`Reminder.Shortlist`, `Reminder.Events`. **Multiset members** (order-insensitive,
duplicate-count-sensitive, following `TagSet`): everything else.

- every record below: two separately-constructed, structurally identical instances compare equal and
  hash equal, and hold non-same collection instances
- every record below: a differing element in each collection member compares unequal

#### Schedule

- `DayTemplate` `Windows` and `EventPrototypes` compare equal regardless of order, and hash equal —
  a Window is a per-day instance, not a position (`CONTEXT.md` § Availability Window)
- `DateOverride.Windows` compares equal regardless of order, and hashes equal
- `DayShape` `Windows` and `Events` compare equal regardless of order, and hash equal
- **`Pattern.Days` compares unequal when reordered** — seven weekday slots, so order is the meaning,
  and `this[DayOfWeek]` indexes them positionally
- two separately-constructed, structurally identical `Pattern`s (same `Days` order, non-same list
  instance) compare equal and hash equal — the positive direction a reorder-only test cannot pin,
  since reference equality also satisfies "reordered ⇒ unequal"
- `PatternBook.Patterns` compares equal regardless of order, and hashes equal
- a `DayTemplate` differing only in `Windows` compares unequal; one differing only in
  `EventPrototypes` compares unequal — not a swap guard: `Windows` and `EventPrototypes` hold
  different element types, so unlike a Dimension's id and its values (#115's swap guard), nothing
  can migrate between the two members for an additive hash to hide

#### Dimensions

- `CategoricalDimension.DeclaredValues` compares equal regardless of order, and hashes equal — a
  categorical axis carries a set, and matching is subset
- **`OrdinalDimension.OrderedValues` compares unequal when reordered** — `RankOf` is the index, so
  a reorder is a different axis
- two separately-constructed, structurally identical `OrdinalDimension`s (same `OrderedValues`
  order, non-same list instance) compare equal and hash equal
- an `OrdinalDimension` differing only in `TaskDefault` or `WindowDefault` compares unequal
- `DimensionRegistry.Dimensions` compares equal regardless of order, and hashes equal
- a `CategoricalDimension` and an `OrdinalDimension` with the same id, label and values compare
  unequal — the derived type is part of the identity, asserted both on the bare records and
  through the `Dimension` union itself (#72 review finding 2 — the bare-record assertion alone
  binds to `object.Equals` post-retrofit and can never fail)
- two separately-constructed, structurally identical `Dimension`s (via `CategoricalDimension`)
  compare equal and hash equal through the union (ADR-0011)
- two separately-constructed, structurally identical `ControlShape`s (via `Slider`) compare equal
  and hash equal through the union (ADR-0011)

#### Tasks

- `CompletionLog.Entries` compares equal regardless of order, and hashes equal — `Latest` is a
  `MaxBy` and `Covers` an `Any`, so nothing in the log reads a position
- a `CompletionLog` holding one entry twice compares unequal to one holding it once
- **`EveryNWeeksOn.Weekdays` compares equal regardless of order**, and hashes equal — a set of
  weekdays, and `EveryNWeeksOn` is compared through `Recurrence`, whose `Rule` is a `OneOf`-style
  closed set
- two separately-constructed, structurally identical `Defer`s (via `AbsoluteDefer`) compare equal
  and hash equal through the union (ADR-0011)
- two separately-constructed, structurally identical `Offset`s (via `BeforeOffset`) compare equal
  and hash equal through the union (ADR-0011)

#### Firing

- `DayFires.Rows` compares equal regardless of order, and hashes equal — the file is keyed on
  `(date, windowId, kind)`, so append order carries nothing

#### Notifications and evaluation contexts

- **`Reminder.Shortlist` compares unequal when reordered** — it is the ranked shortlist, and the
  first line is the top-ranked Task
- **`Reminder.Events` compares unequal when reordered** — date ascending is the stated order
- two separately-constructed, structurally identical `Reminder`s (same `Shortlist` and `Events`
  order, non-empty and non-same list instances) compare equal and hash equal
- `Reminder.FailedFetches` compares equal regardless of order, and hashes equal
- `MatchContext.Fetched` compares equal regardless of Dimension key insertion order and regardless of
  value order within a Dimension, and hashes equal; `FailedFetches` likewise
- a `MatchContext.Fetched` Dimension key mapped to an empty list equals that key being absent, and
  hashes equal — the same rule `TagSet.Dimensions` follows, and `FailedFetches`' own doc already
  treats an unresolved fetch's absence as the empty set
- `DerivedObligationContext`'s `DatedEvents`, `Overrides`, `Completions`, `DayTemplates` and
  `EventExceptions` each compare equal regardless of order, and hash equal
- **two `DerivedObligationContext`s differing only in their `IDayShapeReader` compare unequal** — an
  interface member has no value semantics to compare, so it stays a reference comparison, and this
  test pins that as a decision rather than an oversight

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
- an unknown Opportunity count is a third `ZeroKind`, not a zero and not an absence
- the Status gate still wins over an unknown count

### Day boundary and clock-time resolution

- the day boundary is local midnight in `America/Chicago`, everywhere
- an ambiguous start on the fall-back day resolves to the **first** occurrence
- a nonexistent start in the spring gap **clamps to the gap's end**
- a span crossing a transition is honestly an hour shorter or longer
- a span crossing the spring transition is honestly an hour shorter
- a span crossing the fall-back transition is honestly an hour longer
- **a Window lying entirely inside the spring gap has zero length and does not fire**
- Deadline, Defer and Postpone resolve at the day boundary
- `StartOf` is the given date's own local midnight
- `StartOf` the next day is the same instant as `EndOf` this one, across both DST transitions
- a Window span is empty when the end equals or precedes the start; not empty otherwise
- `ResolveWindow` resolves an ordinary Window to the same two instants `Resolve` would give
- `ResolveWindow` returns `null` for a Window entirely inside the spring gap
- `ResolveWindow` on a Window merely crossing the spring transition still resolves, an hour shorter

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

**Project:** spans two. The adapter half (*a Receipt is retried up to three times while Pushover
has not accepted*, *every failed attempt is logged*) is A1 → `TaskGuide.Infrastructure.Tests`. The
executor half (*`firedAt` written only when Pushover accepts*, *a rejected push reads as unfired
next tick*, *retries stop when the span closes*, *a Receipt is not written to the Fire record*) is
F3 → `TaskGuide.Application.Tests`.

- `firedAt` is written **only when Pushover accepts**
- a rejected push reads as unfired next tick and is retried
- retries stop when the span closes (opportunity) or at the boundary (obligation)
- every failed attempt is logged
- a Receipt is retried up to three times while Pushover has not accepted
- a Receipt accepted on the first attempt is not retried
- a 4xx is never retried
- a short backoff separates the attempts
- a Reminder is never retried by the adapter
- a caller's own cancellation is not swallowed into a retry
- a Receipt attempt that exceeds its budget is refused and retried
- a 4xx names the Task in the log
- a Receipt is not written to the Fire record

### Notification content

**Project:** F1, `Reminder.For` in `Domain/Notifications/` → `TaskGuide.Domain.Tests`.

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

### FetchOutcome (#76)

- a `Known` outcome matches to the known arm and yields its value
- an `Unavailable` outcome matches to the unavailable arm and yields its reason

### Weather, the fetched axis

**Project:** spans two. *No weather-tagged Active Task ⇒ no API call* is F6, executor state →
`TaskGuide.Application.Tests`. The rest (current vs forecast, unknown fails closed, the
UI-visible footer note) is A2 → `TaskGuide.Infrastructure.Tests`.

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

**Project:** spans three tickets in two lanes — #79 the Domain rule (`GlanceState` equality,
`ShouldSend`) → `TaskGuide.Domain.Tests`; #84 the executor's scheduling state (recomputed every
tick, the 30-minute floor, the one retry) → `TaskGuide.Application.Tests`; #88 the renderer →
`TaskGuide.Infrastructure.Tests`. The one-test-file-per-section rule does not hold for this
section.

- `GlanceState` equality (#76): two structurally equal states built on distinct-but-equal
  `Shortlist` instances compare equal — the reference-equality trap a positional record falls
  into on an `IReadOnlyList` member
- `GlanceState` equality (#115): the same holds when the `Shortlist`'s `TaskItem`s or the
  `ResolvedWindow`'s `AvailabilityWindow` carry separately-constructed, structurally-equal
  Tag-bearing `TagSet`s (not the shared `TagSet.Empty` instance), for both `InsideWindow` and
  `NextWindow`
- a genuine difference (a different `MatchingNow`, a different shortlist member) still compares unequal
- recomputed every tick, sent only when the payload differs from the last one **sent**
- not sent again inside 30 minutes
- **a Window start preempts the floor**
- **a Window end does not**
- between Windows it shows the next Window's start, weekday included when not today
- inside a Window with no match it shows the same fall-through shape
- it is never blank
- one retry at the next tick, ignoring the floor; never two

### Liveness

**Project:** A3 → `TaskGuide.Infrastructure.Tests`, except the first bullet (*`/health` reports
`{ ok, lastTick, storage, uptime }`*), which is the endpoint → `TaskGuide.Api.Tests`.

- `/health` reports `{ ok, lastTick, storage, uptime }`
- a stalled loop reports `ok: false` while HTTP still answers
- **read health parses the file** — a truncated or empty file reports unreadable where `stat` would
  have passed
- write health is read off the retention sweep's outcome, not a probe
- a registry collision signals outbound before exiting
- load, memory and Pushover reachability appear nowhere in the predicate

### Test support (#77)

**Project:** `TaskGuide.Application.Tests` — the shared doubles in `TaskGuide.TestSupport` are
production code for every lane after Wave 0, so they carry their own tests. The `DayShapeReader`
bullet below is `TaskGuide.Infrastructure.Tests`, which can reference `TaskGuide.Infrastructure`
directly.

- an unseeded `FakeStoreView` reads empty on every member except the default Pattern
- **an unseeded `FakeStoreView`'s default Pattern resolves to a Day template present in
  `DayTemplates`** — the same three steps `DayShapeReader.For` takes, so an unseeded view no
  longer throws on the central read path every later lane exercises (#77 review finding 1)
- a seeded `FakeStoreView` reads back exactly what it was given
- `CompletionsFor` on an unseeded Task is an empty log, not a throw
- `FiresOn` on an unseeded date is empty, not a throw
- an applied mutation is recorded and returns `Applied`
- an applied write is visible to the next `Read`
- **the mutation lambda is handed the view as it stands at call time** — not one read earlier
- a refused mutation returns the refusal, writes nothing, and is not recorded
- `LastWriteSucceeded` is `null` before any write and `true` after one
- `LastWriteSucceeded` is untouched by a refusal and by an empty write list
- an unrecognised write payload throws `NotImplementedException`, naming its type — matching
  `JsonStore`'s type and message shape for the same programming error (#77 review finding 6)
- a write that throws mid-apply leaves `Mutations` empty — a mutation is recorded only once it
  has actually applied (#77 review finding 4)
- a `PatternsWrite`'s `Patterns` list is deep-copied, not stored by reference (#77 review finding 2)
- a `CompletionLogWrite`'s `Entries` list is deep-copied, not stored by reference (#77 review finding 2)
- a `FiresWrite`'s `Rows` list is deep-copied, not stored by reference (#77 review finding 2)
- **concurrent `MutateAsync` calls serialise, so none of their writes are lost** (#77 review finding 3)
- `MutateAsync` throws for an already-cancelled token (#77 review finding 7)
- `FailNextWrite` makes the next write throw, reports `LastWriteSucceeded` false, and applies
  nothing (#77 review finding 5)
- `FailNextWrite` only fails the next write, not the one after it
- **an unseeded `FakeStore` handed to `DayShapeReader` returns a usable `DayShape`** — the real
  end-to-end check for #77 review finding 1, run against the actual reader rather than the proxy
  above
- a recording sender records what it was handed and reports success
- a recording sender reports the failure it was configured for, without throwing
- an unconfigured `FakeWeatherSource` is `Unavailable` on both axes
- a configured `FakeWeatherSource` yields its known value and records the call
- `FakeWeatherSource.CurrentAsync` throws for an already-cancelled token (#77 review finding 7)
- `FakeWeatherSource.ForecastAsync` throws for an already-cancelled token (#77 review finding 7)
- an unseeded date reads an empty `DayShape`, and the read is recorded
- a recording heartbeat keeps every tick instant in order
- a written fire row survives the next unrelated mutation
- a completion log seeded for a task absent from Tasks survives the next mutation
- a view already built is unaffected by a later `With…` call on the same builder
- `WithDayTemplates` re-points the builder's default Pattern at the first seeded template, so a
  view seeded with templates alone still resolves (#116)
- an explicit `WithPatterns` wins over that re-pointing, in either call order (#116)
- a `DayTemplatesWrite` through `MutateAsync` re-points the builder's default Pattern the same way,
  while the builder's default pair is still intact (#116)
- a `DayTemplatesWrite` leaves a caller-supplied Pattern book exactly as it was, matching
  `JsonStore`, which does no fix-up (#116)
- a `DayTemplatesWrite` that empties `DayTemplates` leaves it empty and its Pattern book
  unresolvable, matching a fresh `JsonStore` (#116)
- once real Day templates have been seeded, a `DayTemplatesWrite` leaves the derived Pattern book
  alone, so an orphaned template surfaces as it would in production (#116)
- the builder's default pair survives an unrelated write, so a later `DayTemplatesWrite` still
  re-points (#116)
- a write that throws part-way through `OrderedWrites` reports `LastWriteSucceeded` false,
  matching `JsonStore` (#116)
- an unrecognised payload as the very first write leaves `LastWriteSucceeded` untouched,
  matching `JsonStore` (#116)

---

## Sequential · `TaskGuide.Storage.Tests`

Against `fixtures/data`, the golden store.

*The five stamp/promote/delete bullets below (from "a date materialised mid-day..." through
"deleting an `Unused` template corrupts no record") are uncovered between this ticket and S1,
their tests deleted (#77) because they enacted the rule in their own bodies rather than testing
production behaviour — accepted knowingly, since the deleted tests never detected anything.*

- the whole store loads into typed objects at startup
- every read is served from memory
- **a mutation writes the affected file(s) before the request returns**
- a write is atomic: a killed process leaves the old file or the new one, never a partial
- one global write lock serialises mutations
- **a `MutateAsync` refusal decided inside the write lock writes nothing** — the on-disk file and
  the in-memory view are unchanged, `LastWriteSucceeded` stays `null` (no write was attempted),
  and the lock is released, not held, so the next mutation still lands
- an applied `MutateAsync` mutation returns `Applied`
- **a read never blocks on a write**
- `tasks.json` round-trips with no `status` field
- every `RecurrenceRule` kind round-trips through its own JSON string (#72 review finding 1 —
  `TaskCodec`'s `WriteRule` binds its arms to the union's type arguments by position)
- `day-templates.json` round-trips the golden store unchanged
- a Window's start and end round-trip as authored clock times, never as instants
- an Event prototype's `absenceNotice` Offset round-trips, and a null one stays null
- no codec writes a `status` property, whatever type it would carry — `TaskCodec`
- no codec writes a `status` property, whatever type it would carry — `DayTemplateCodec`
- `patterns.json` round-trips the golden store unchanged
- a Pattern's seven days are indexed by weekday with Sunday first
- a Pattern book whose `days` array is not seven long is rejected at read, naming the Pattern
- an active Pattern id matching no Pattern throws, naming the id — a dangling reference is not
  absence (ADR-0010b)
- no codec writes a `status` property, whatever type it would carry — `PatternCodec`
- `overrides.json` round-trips the golden store unchanged
- a one-off day round-trips with a null `used`
- no codec writes a `status` property, whatever type it would carry — `OverrideCodec`
- `events.json` round-trips the golden store unchanged
- an Event's loose Tags survive the round trip, and are what a derived-obligation rule reads
- `event-exceptions.json` round-trips both the delete row and the edit row
- an Event exception that is neither a delete nor an edit is rejected at read, naming its date
- two Event exceptions sharing `(date, prototypeId)` are rejected at read, naming both — otherwise
  that date becomes permanently unreadable (ADR-0010a)
- an Event's `absenceNotice` round-trips, and a null one stays null
- no codec writes a `status` property, whatever type it would carry — `EventCodec`
- `manifest.json` version mismatch runs the ordered N→N+1 steps at startup
- a store already at `CurrentVersion` runs no migration step and takes no snapshot
- a version ahead of this binary refuses to start, named — a rollback must not silently
  down-migrate
- `manifest.json` is written only after every migration step succeeds
- a migration step that does not move the version strictly forward is rejected where it is built —
  the cycle that would hang the walk cannot be constructed (ADR-0009), so this is a property of
  `StoreMigration`, not of a startup run
- `StoreMigration` is not a record: `with` would be a second door around that invariant
- a migration walk that would overshoot `CurrentVersion` refuses to start
- **every conscious refusal at startup leaves the data directory exactly as it found it** (ADR-0009)
  — asserted by listing the whole directory, not by checking named files absent: registry collision,
  version ahead, walk overshoot
- startup against a fresh `/data` creates `manifest.json` without snapshotting
- the registry sweep makes no `MutateAsync` call when nothing moved
- the registry sweep promotes a loose Tag the registry now claims, and writes the change
- the registry sweep promotes a loose Tag on a Day template Window
- an empty `/data` starts and the active Pattern resolves without throwing
- a fresh `/data` seeds one vanilla weekly Pattern of a single plain Day template
- the default Pattern seed takes no snapshot
- a store that already has a Pattern is never reseeded
- the plan phase returns its refusal rather than throwing, and writes nothing (#78)
- a valid plan snapshots, migrates, stamps the manifest, then writes, in that order (#78)
- the runtime store opened after the write phase reads what the write phase landed (#78)
- an empty `/data` bootstraps and `IDayShapeReader` returns a usable `DayShape` (#78)
- `manifest.json` round-trips its version
- a snapshot is written once per startup, and **only when that startup will write**
- snapshots keep the last 5
- a Snapshot is a whole-file copy, not a re-serialisation
- a Snapshot recreates the relative directory structure of the paths it is given
- an Event-plus-Override write puts the **Event first**
- a crash between the two leaves the state the overlap check detects, and the next read re-offers
  the prompt
- a mutation writes every affected file before the request returns, not only the first
- a partially-failed multi-file write leaves `LastWriteSucceeded` false and does not swap the view
- a write of one collection leaves every other collection in the swapped-in view unchanged
- an unrecognised write payload before any write leaves `LastWriteSucceeded` untouched
- an unrecognised write payload after a successful write sets `LastWriteSucceeded` false
- an empty write list leaves `LastWriteSucceeded` untouched — no write is not a false success
- an unknown field written by a newer binary **is not preserved** across a load, mutate and
  save round trip — the channel was removed everywhere (ADR-0001, *Rollback is lossy, and
  that is accepted*); this is the test that fails if someone re-adds it
- an Override's copy **preserves each Window's id**
- a date materialised mid-day does not re-fire an already-fired Window
- an Override carries its `used` record with the template **name as it was**
- the use record survives the date becoming a one-off day
- re-stamping replaces the use record rather than appending
- promoting a one-off day writes the source date's use record and **does not re-link**
- `Unused` is false for a template referenced only by a **dormant** Pattern
- `Unused` is false for a template stamped within ±13 months, in **either** direction
- deleting an `Unused` template corrupts no record
- a template stamped 14 months ago is `Unused`
- a template stamped 14 months ahead is `Unused`
- an Override span of one date yields exactly that date
- an Override span yields every date inclusive of both ends, in ascending order
- fires older than 30 days are unlinked as whole files
- a fire file exactly 30 days old is kept (the boundary must not drift)
- a file in `fires/` whose name is not a date is left untouched
- the sweep on an absent `fires/` directory is a no-op, not an error
- a per-file delete failure is recorded and the sweep keeps going
- a fire row carries the Window's name and span **as they were**
- `(date, null, "fallback")` is unique per day
- a completion log is not rewritten when its Task's title changes
- each completion log round-trips the golden store unchanged
- a one-off Task's entry round-trips a null `due`
- `completions/derived.json` round-trips, keyed on `ruleId` + `triggerId` + `due`
- two derived completions sharing `(ruleId, triggerId, due)` are rejected at read, naming all three
  (ADR-0010a)
- the Task id comes from the filename, so a log file carries no id of its own
- no codec writes a `status` property, whatever type it would carry — `CompletionCodec`
- `fires/2026-08-15.json` round-trips the golden store unchanged
- `dueAt` and `firedAt` round-trip as instants while `windowStart` and `windowEnd` round-trip as clock times, in the same file
- a pending Snooze row round-trips with a null `firedAt` and reads `IsPendingSnooze`
- Fire dates are read from fire file names without parsing contents
- no codec writes a `status` property, whatever type it would carry — `FireCodec`
- two fire rows differing only in `windowId` both load — the key is the whole `(windowId, kind)`,
  not the kind alone
- two fire rows sharing `windowId` and `kind` are rejected at read, with the date named — the
  uniqueness rule is general, not a fallback special case
- a fire file name whose date is not exactly `yyyy-MM-dd` is not a fire file
- every `FireKind` round-trips through its own JSON string
- a restore under a running service is invisible, and the next mutation destroys it *(the one test
  that documents a failure mode rather than preventing it — see #49's restore drill)*
- every minted id carries its type's prefix and 26 Crockford Base32 characters
- ids minted in sequence sort lexicographically in mint order
- two ids minted in the same millisecond still differ
- a minted id is accepted by its own `IPrefixedId` record struct round-trip
- a missing collection file loads as empty rather than throwing — a fresh `/data` is valid
- a corrupt collection file fails at registration, not first use, for a collection other than
  `tasks.json`
- a date with no Override takes the active Pattern's template for its weekday
- a Pattern naming an absent Day template throws, naming the template, the Pattern and the date
  (ADR-0010b)
- a date with an Override takes the Override's Windows and reads `IsOverridden`
- an Override with zero Windows is a shape, not an absence — `IsOverridden` is true and the Pattern's Windows do not leak through
- a dated Event on the date appears in the shape
- a recurring instance from the weekday's Event prototype appears in the shape
- a deleted instance's Event exception drops it
- an edited instance's Event exception replaces its name and span, leaving the prototype untouched
- an Event exception for a different prototype on the same date changes nothing
- reading a day's shape writes nothing — no Override is materialised and `MutateAsync` is never called
- a recurring instance's Event id is the same on two reads of the same date

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
- `PUT /api/overrides/{date}/stamp` copies the template's shape and **preserves each Window's id**
- `PUT /api/overrides/{date}/stamp` is refused for an unknown template id
- `POST /api/overrides/{date}/promote` writes a new Day template and **does not re-link** the source
  date, which keeps its own copy
- `POST /api/overrides/{date}/promote` leaves the source date with a **use record**, so the promoted
  template is not born `Unused`
- `DELETE /api/day-templates/{id}` is refused while the template is in use, and accepted when it is
  `Unused`
- `GET /api/day-templates/{id}/usage` names every Pattern referencing the template — dormant ones
  included
- `PATCH /api/day-templates/{id}/windows/{windowId}` edits that Window only and **does not propagate**
  to a same-named Window in another template
- `GET /api/day-templates/{id}/windows/{windowId}/dependents` counts the Tasks that would be orphaned
  by removing a Dimension value, **before** the edit is saved, and writes nothing
- `POST /api/events` writes the Event **first**, then the one-off day its overlap resolution generates
- `GET /api/events/overlap-check` names every Window the proposed Event overlaps, **partial overlaps
  included**, and writes nothing
- `PUT /api/event-exceptions/{date}/{prototypeId}` records a **move** as an edit, not as
  delete-plus-create, and **stamps no Override**
- `DELETE /api/event-exceptions/{date}/{prototypeId}` on an instance the active Pattern no longer
  assumes matches nothing and is not an error
- `/health` is reachable without traversing `/api`
- host creation refuses a future-version store, before any endpoint or the tick loop can start (#78)

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

### Shared controls

- `screensFor` orders registered screens by `order` then `id`
- `screensFor` returns nothing for a tab with no registrations, and never returns another tab's
  screens
- registering a duplicate screen id throws
- the quick-action slot is `null` until something registers, then returns that renderer
- registering a second quick action throws
- each screen module self-accepts its Vite update and `registerScreen` unregisters only that ID on
  dispose, preserving sibling registrations; `installHmrGuard` invalidates an updated `App.tsx`
  for a full reload, and both no-op when hot is absent (Vite's module graph itself remains manual
  verification)
- the tasks.screen module registers "tasks" on the tasks tab, rendering `TasksScreen` — asserted
  directly against the module's own import, since App.test.tsx resets the registry before any
  test and never exercises this real wiring
- the tab bar renders all four tabs
- `ScreenNav` renders the title, and the back control only when `back` is given
- `ScreenNav` renders the back control from a `BackProvider` ancestor when no explicit `back` prop
  is given, and an explicit prop wins over that ancestor
- `ScreenNav` renders the registered quick action in the nav's right slot; renders nothing there
  when nothing is registered
- `ScreenNav`'s quick-action slot renders as its own component instance rather than a bare inlined
  function call, so a renderer with its own hook doesn't risk a Rules of Hooks violation when it
  registers after ScreenNav has already rendered once
- `PlaceholderScreen` renders its title and the not-built-yet message, and the registered quick
  action — a placeholder tab is a screen too
- a tab with no registered screen renders the placeholder
- a tab with exactly one registered screen renders it directly
- a tab with more than one registered screen renders an index of titles, and selecting one shows
  it with a working back affordance
- a selected screen is not double-wrapped in a second `ScreenNav` — every screen renders its own,
  and the shell supplies the back action via `BackProvider`/context rather than wrapping it again
- a freshly registered screen appears with zero changes to `App.tsx`
- the registered quick action appears once in the multi-screen index, once in a selected screen,
  on more than one active tab including a placeholder tab, and on a single-screen tab via that
  screen's own `ScreenNav`
- a non-OK response throws an error naming the method, path, and status
- a 204 response is treated as absence, not a parse error
- a 200 response parses as JSON
- `DateEntry` renders a null value as blank and a given ISO value verbatim
- `DateEntry` reports the new ISO value on change, and `null` when cleared
- `DateEntry`'s date input survives its own input event — same DOM node before and after
- `RecurrenceEditor` renders no rule as "does not repeat"
- changing `RecurrenceEditor`'s kind reports a fresh rule for that kind, with its own defaults
- changing the N field on an N-based rule updates only `n`
- every non-matching `RecurrenceEditor` sub-field group carries the `hidden` attribute (the JS
  half of the contract — `index.css`'s `[hidden] { display: none !important }` is the other half,
  needed because `.stack`/`.chipset`'s own `display: flex` otherwise beats the UA `[hidden]` rule
  regardless of specificity; that CSS half is E2E's to hold, since jsdom never loads index.css)
- emptying "Every N" does not commit `n: 0`; typing an out-of-range "Day of month", "Month", or
  yearly "Day" does not commit it either — `min`/`max` on a number input only constrain the
  spinner, not a typed value
- a completion-anchored rule renders the first-due date entry
- `RecurrenceEditor`'s kind `<select>` survives its own change, and survives a sub-field's change
- `OrdinalSlider` renders a labelled tick for every value, in order, and a hint naming the set
  value
- `OrdinalSlider` reports the value at the new slider index on change
- `OrdinalSlider` shows a "leave at the default" toggle when a default is declared, pressed while
  unset, and choosing it clears the value to `null`
- `OrdinalSlider` has no "leave at the default" control when no default is declared
- `OrdinalSlider` dims the slider with a class (never inline style) and shows index 0 while unset,
  with a hint explaining the default; touching the slider while unset still commits a value
- committing index 0 while unset (no `change` event fires, since the thumb already sits there) on
  `pointerUp` still commits the least value, without remounting the slider or double-committing
  once a value is already set
- `OrdinalSlider` falls back to the unset presentation — dimmed, index 0, no false "Set to" claim
  — when `value` isn't present in `values` at all
- `OrdinalSlider` renders read-only with the same control structure — ticks, hint, and toggle
  included — every control disabled
- `OrdinalSlider`'s range input survives its own input event and a press of the default toggle —
  same DOM node throughout
- changing `RecurrenceEditor`'s kind away from a completion anchor clears the first-due date;
  changing between two completion-anchored kinds leaves it alone
- a rejected keystroke in a `RecurrenceEditor` number field leaves the field showing what was
  typed, not the previous committed value re-inserted ahead of it — clearing "3" and typing "12"
  commits 12, never 312

## `TaskGuide.E2E`

- the landing page loads (#74 ARM64/Debian 12 Playwright smoke check)
- capture a Task in the SPA, see it in the list, mark it off
- open a Reminder landing page, snooze it, see the re-fire
- **the date picker survives its own input events** on every date-entry surface — Deadline, Defer's
  absolute form, Postpone's escape, Recurrence's first-due, an Event's date, and the Override
  rail's "Pick a date…"
- a `<select>` and an ordinal slider survive the same way
- an ordinal slider commits its least value from the keyboard alone, and a press of "leave at the
  default" released over the slider does not commit one
- authoring an Override over a range from the rail's escape writes the whole span

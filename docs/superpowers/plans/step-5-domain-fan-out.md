# Plan — build-plan step 5: implement the pure domain functions

**Spec:** `CONTEXT.md` (read by line range via `CONTEXT-INDEX.md`) plus `docs/adr/`.
**Test list:** `tests/TEST-INVENTORY.md` § "Fan-out · `TaskGuide.Domain.Tests`" — 109 tests.
**Branch:** `walking-skeleton`.

Today `src/TaskGuide.Domain/` is signatures: ~26 members throw `NotImplementedException`, and
`tests/TaskGuide.Domain.Tests/` contains zero test files. This plan turns both around, TDD, one
lane at a time.

---

## Global Constraints

These bind every task. A reviewer checks each one.

1. **TDD, red first.** Write the test, run it, watch it fail *for the right reason* — an assertion
   failure, not a compile error and not a fixture error. Only then implement. The report must quote
   the verbatim red output and the verbatim green output.
2. **A test that passes against a broken implementation is worth less than no test.** If an
   assertion could hold whatever the implementation does, mutate the implementation deliberately,
   confirm the test goes red, and revert. Say so in the report.
3. **Test names come verbatim from `tests/TEST-INVENTORY.md`**, snake_cased into C# method names
   (the existing convention: `tests/TaskGuide.Storage.Tests/JsonStoreTests.cs`). Any test added
   beyond the inventory gets a new line appended to the inventory in the same commit.
4. **`CONTEXT.md` wins over the inventory** wherever the inventory paraphrases.
5. **Never read `CONTEXT.md` whole** — 122 KB. Read only the `sed -n 'A,Bp'` ranges the task names.
6. **Facts stored, everything else derived.** Status, Opportunities, Orphan-ness and `Unused` are
   computed on read and never persisted. No `Status` setter, no priority field, no `Overdue` state,
   no `Deferred` status (ADR-0004, ADR-0007).
7. **New behaviour is a new rule, not configuration.** No settings file, no knob, no tunable weight.
   If you are adding one, you are contradicting an ADR — say so instead of doing it.
8. **Stay in your file lane.** Touch only the files your task names, plus your own new test file.
   If your lane genuinely needs a change in another lane's file, report it rather than making it.
9. **Pure functions only.** No I/O, no clocks read from the ambient environment, no static mutable
   state. `now` is always a parameter.
10. **Timezone is `America/Chicago`, always, via `DayBoundary.ZoneId`.** Nothing in the model may
    disagree about what day it is.
11. **Build and test with `dotnet test tests/TaskGuide.Domain.Tests`** from the repo root. The whole
    suite (`dotnet test`) must stay green; you inherit 37 passing .NET tests.
12. **Commit your own work** — one commit for the lane, message naming the inventory section.
    Never push.
13. **Do not dispatch subagents.** Review arrives from the controller.
14. `JsonStore.cs` and `UlidIdMinter.cs` unimplemented members are out of scope for every task here.

## Existing conventions

- xUnit, `[Fact]`/`[Theory]`, `Assert.*`. Test methods are `Snake_case_sentences`.
- One test class per lane, `sealed class`, XML doc comment naming the inventory section it covers.
- Namespace `TaskGuide.Domain.Tests`.
- Records everywhere in the domain; prefer expression-bodied members.

---

# Wave 1 — depends on nothing

## Task 1: Offset

**Files:** `src/TaskGuide.Domain/Tasks/Offset.cs`, new
`tests/TaskGuide.Domain.Tests/OffsetTests.cs`.

**Read:** `sed -n '263,280p' CONTEXT.md` (Offset), `sed -n '157,204p' CONTEXT.md` (Defer).

**Implement:** `BeforeOffset.ResolveAgainst`, `LastWeekdayBefore.ResolveAgainst`.

**Tests (inventory § Offset, 4):**
- `N days/weeks/months before` resolves against its anchor
- `the last Friday strictly before` a Friday anchor resolves to the **previous** week
- `the last Friday strictly before` a Saturday anchor resolves to the day before
- a month-unit offset from the 31st lands on a real date

Note test 4: a month-unit offset must not throw and must not silently roll into the next month.
`CONTEXT.md`'s Offset entry is the authority on which real date it lands on.

## Task 2: Day boundary and clock-time resolution

**Files:** `src/TaskGuide.Domain/Time/DayBoundary.cs`,
`src/TaskGuide.Domain/Time/ClockTimeResolution.cs`, new
`tests/TaskGuide.Domain.Tests/DayBoundaryTests.cs`.

**Read:** `sed -n '864,884p' CONTEXT.md` (Day boundary), `sed -n '589,634p' CONTEXT.md`
(Availability Window). ADR-0005.

**Implement:** `DayBoundary.DateOf`, `DayBoundary.EndOf`, `ClockTimeResolution.Resolve`,
`ClockTimeResolution.LengthOf`.

**Tests (inventory § Day boundary and clock-time resolution, 6):**
- the day boundary is local midnight in `America/Chicago`, everywhere
- an ambiguous start on the fall-back day resolves to the **first** occurrence
- a nonexistent start in the spring gap **clamps to the gap's end**
- a span crossing a transition is honestly an hour shorter or longer
- **a Window lying entirely inside the spring gap has zero length and does not fire**
- Deadline, Defer and Postpone resolve at the day boundary

US 2026 transitions: spring forward 2026-03-08 (02:00→03:00), fall back 2026-11-01
(02:00 repeats 01:00–02:00). Verify these against `TimeZoneInfo` rather than trusting this note.

## Task 3: Dimension registry

**Files:** `src/TaskGuide.Domain/Dimensions/DimensionRegistry.cs`, new
`tests/TaskGuide.Domain.Tests/DimensionRegistryTests.cs`. You may read but must not edit
`Dimension.cs` / `KnownDimensions.cs`.

**Read:** `sed -n '339,473p' CONTEXT.md` (Dimension), `sed -n '474,555p' CONTEXT.md` (Tag).

**Implement:** `DimensionRegistry.Claiming`, `DimensionRegistry.AssertNoDuplicateValues`.

**Tests (inventory § Dimension registry, 5):**
- **a registry declaring one value on two Dimensions refuses to start**, naming the value
- a duplicate is rejected at startup, not resolved at the point of use
- identity and label are independent: renaming the label touches no stored Tag
- a categorical Dimension derives a multi-select control; an ordinal one derives a slider
- an ordinal Dimension declaring a default derives a "leave at the default" control; **Duration,
  declaring none, derives no such control**

The last two are about a *derived control shape*, which does not exist yet. Add the smallest
pure query that answers them on `Dimension` — a derived property or a small `ControlShape`
discriminated result in `Dimensions/`. Do not build UI. Do not add configuration (Constraint 7):
the shape is derived from the algebra, never authored.

## Task 4: Recurrence

**Files:** `src/TaskGuide.Domain/Tasks/Recurrence.cs`, `src/TaskGuide.Domain/Tasks/Completion.cs`,
new `tests/TaskGuide.Domain.Tests/RecurrenceTests.cs`.

**Read:** `sed -n '281,338p' CONTEXT.md` (Recurrence), `sed -n '47,66p' CONTEXT.md` (Task).

**Implement:** the generator. `Recurrence` today is a data record with no behaviour — add the
pure functions the tests need (e.g. a `RecurrenceRules` static or methods on `Recurrence`):
next/current instance deadline from `(rule, anchor, firstDue, createdAt, completionLog, now)`,
the grace rule, and which instance a completion satisfies.

**Tests (inventory § Recurrence, 10):**
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

---

# Wave 2 — consumes wave 1

## Task 5: Status and eligibility

**Files:** `src/TaskGuide.Domain/Tasks/Status.cs`, `src/TaskGuide.Domain/Tasks/Defer.cs`, new
`tests/TaskGuide.Domain.Tests/StatusTests.cs`.

**Read:** `sed -n '67,140p' CONTEXT.md` (Status), `sed -n '141,156p' CONTEXT.md` (Deadline),
`sed -n '157,204p' CONTEXT.md` (Defer), `sed -n '205,262p' CONTEXT.md` (Postpone),
`sed -n '556,588p' CONTEXT.md` (Drift). **ADR-0007 is binding here.**

**Implement:** `StatusRules.Of`, `StatusRules.IsEligible`, `StatusRules.AgeOf`,
`OffsetDefer.Resolve`.

**Tests (inventory § Status, 10 + § Eligibility and the two clocks, 7):**
- a Task with a completion entry covering the current instance reads `Done`
- a Task with no Duration reads `Unprocessed`
- **a Task with no Duration that is also 59 days old reads `Unprocessed`, not `Stale`**
- a Task with no Duration that is also completed reads `Done` — completion outranks everything
- an undeadlined one-off Task aged past the threshold reads `Stale`
- **a one-off Task with a Deadline is never staled by age**, however old
- a recurring Task with N consecutive missed instances reads `Stale`
- a recurring Task with N-1 consecutive misses and one completion between them reads `Active`
- a Task past its Deadline reads `Active` — overdue is not a state
- nothing in the model can write a Status; the type exposes no setter and storage carries no field
- `eligible = Active AND now >= Defer AND now >= Postpone`, every term computed on read
- a deferred Task is absent from every match-driven surface but present in the task list
- `age = now - max(CreatedAt, Defer)` — Defer pauses the age clock
- **Postpone does not pause the age clock**
- a postponed Task cannot also be deferred-in-the-future — the gesture only reaches Tasks whose
  Defer has elapsed, so `max` needs no third term
- an offset Defer on a recurring Task resolves against the generated Deadline, per instance
- a recurring Task rejects an absolute Defer

Status ordering is first-match-wins and the order **is** the rule.

## Task 6: Matching

**Files:** `src/TaskGuide.Domain/Matching/Matcher.cs`, new
`tests/TaskGuide.Domain.Tests/MatcherTests.cs`.

**Read:** `sed -n '1566,1577p' CONTEXT.md` (Matching rule), `sed -n '339,473p' CONTEXT.md`
(Dimension), `sed -n '474,555p' CONTEXT.md` (Tag), `sed -n '589,634p' CONTEXT.md`
(Availability Window). ADR-0004, ADR-0007.

**Implement:** `Matcher.CategoricalFits`, `Matcher.OrdinalFits`, `Matcher.Fits`.

**Tests (inventory § Matching, 6 categorical rows + 8 rules = 14 cases; the inventory's table is
one test each — a `[Theory]` with six `[InlineData]` rows satisfies it):**

| Task | Window | Fits |
|---|---|---|
| `{}` | `{}` | yes |
| `{}` | `{garage}` | yes |
| `{garage}` | `{}` | **no** |
| `{garage}` | `{garage, outside}` | yes |
| `{Sam, Ana}` | `{Sam}` | **no** |
| `{Sam, Ana}` | `{Sam, Ana, the kids}` | yes |

- ordinal: task value ≤ window value fits; above it does not
- an ordinal axis silent on the Task side takes the task-side default
- an ordinal axis silent on the Window side takes the window-side default
- **a categorical axis has no default on either side**
- matching is a conjunction across axes: failing one axis fails the Task
- a rule reads only its own axis
- **loose Tags are ignored by matching** on both sides
- a mistyped Tag (`#garge`) admits the Task to *more* Windows, not fewer

## Task 7: Duration as a derived ceiling

**Files:** new `src/TaskGuide.Domain/Matching/DurationCeiling.cs`,
`src/TaskGuide.Domain/Schedule/AvailabilityWindow.cs`, new
`tests/TaskGuide.Domain.Tests/DurationCeilingTests.cs`. **Do not edit `Matcher.cs`** — Task 6
owns it.

**Read:** `sed -n '339,473p' CONTEXT.md` (Dimension), `sed -n '474,555p' CONTEXT.md` (Tag),
`sed -n '47,66p' CONTEXT.md` (Task), `sed -n '589,634p' CONTEXT.md` (Availability Window).
ADR-0007.

**Implement:** the Window's Duration ceiling derived from its resolved length (use
`ClockTimeResolution.LengthOf` from Task 2), and raw-minutes → bucket snapping.

**Tests (inventory § Duration as a derived ceiling, 4):**
- a 45-minute Window admits the 30 bucket and below, and not 60
- a Window's ceiling is derived from its length and cannot be authored
- raw minutes from a capture path snap **up** to the next bucket (45 → 60)
- 61 minutes snaps to `Longer`

Buckets are `KnownDimensions.DurationBuckets`: 2 / 10 / 30 / 60 / longer.

## Task 8: The promote/demote sweep

**Files:** new `src/TaskGuide.Domain/Dimensions/RegistrySweep.cs` (name it as you see fit inside
`Dimensions/`), `src/TaskGuide.Domain/Tags/TagSet.cs`, new
`tests/TaskGuide.Domain.Tests/PromoteDemoteSweepTests.cs`. **Do not edit `DimensionRegistry.cs`**
— Task 3 owns it.

**Read:** `sed -n '339,473p' CONTEXT.md` (Dimension), `sed -n '474,555p' CONTEXT.md` (Tag).

**Implement:** the pure sweep over a `TagSet` given a registry — promote loose Tags matching a
declared value into that Dimension's slot; demote withdrawn values back into the loose bag.

**Tests (inventory § The promote/demote sweep, 5):**
- declaring a Dimension value matching a loose Tag moves it into that Dimension's slot on every
  Task and Window carrying it
- withdrawing a value returns those Tags to the loose bag **with their strings intact**
- promote-then-demote is lossless — the round trip is identity
- an **ordinal** axis takes up a loose Tag only if the record has no value on that axis
- a deliberately-set ordinal value is never overruled by a loose Tag

The sweep is pure here: it takes a `TagSet` and returns one. Wiring it to startup is a later,
sequential task — do not touch `Program.cs`.

---

# Wave 3 — consumes wave 2

## Task 9: Opportunities and the horizon

**Files:** `src/TaskGuide.Domain/Ranking/Opportunities.cs` (the `OpportunityCounter` half only —
leave `OrphanDetection` alone; Task 13 owns it), new
`tests/TaskGuide.Domain.Tests/OpportunitiesTests.cs`.

**Read:** `sed -n '1751,1759p' CONTEXT.md` (Opportunities), `sed -n '1760,1861p' CONTEXT.md`
(Scarcity), `sed -n '589,634p' CONTEXT.md` (Availability Window), `sed -n '686,711p' CONTEXT.md`
(Pattern), `sed -n '712,779p' CONTEXT.md` (Override), `sed -n '780,832p' CONTEXT.md` (Event).
ADR-0004.

**Implement:** `OpportunityCounter.CountAhead`, `OpportunityCounter.CountInPatternWeek`. It walks
real dates through `IDayShapeReader`; supply a fake reader in the test.

**Tests (inventory § Opportunities and the horizon, 8):**
- without a Deadline the horizon is a true rolling 7 × 24h
- a once-a-week opportunity counts **exactly once** whatever hour it is asked
- with a Deadline ahead the horizon runs to the end of that day
- **with a Deadline passed the bound is dropped** and the horizon reverts to a rolling 7 days
- an overdue Task therefore never misreports as an Orphan
- the count walks real dates, so an Override removing the only admitting Window drops it
- a dated Event displacing a Window drops it
- switching the active Pattern moves the count

## Task 10: Snooze arithmetic

**Files:** `src/TaskGuide.Domain/Firing/Snooze.cs`, new
`tests/TaskGuide.Domain.Tests/SnoozeTests.cs`.

**Read:** `sed -n '1436,1565p' CONTEXT.md` (Snooze), `sed -n '864,884p' CONTEXT.md` (Day
boundary), `sed -n '885,1023p' CONTEXT.md` (Firing). **ADR-0005 is binding here.**

**Implement:** `SnoozePolicy.IntervalFor`, `SnoozePolicy.IsOffered`, `SnoozePolicy.RemainingIn`,
plus the re-derived ceiling and the frozen-other-dimensions rule.

**Tests (inventory § Snooze arithmetic, 9):**
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

## Task 11: Derived-obligation rules

**Files:** `src/TaskGuide.Domain/Rules/DerivedObligationRule.cs`, new
`tests/TaskGuide.Domain.Tests/DerivedObligationRuleTests.cs`.

**Read:** `sed -n '1578,1691p' CONTEXT.md` (Derived-obligation rule),
`sed -n '1692,1700p' CONTEXT.md` (Rules generally), `sed -n '263,280p' CONTEXT.md` (Offset),
`sed -n '780,832p' CONTEXT.md` (Event), `sed -n '833,863p' CONTEXT.md` (Event exception),
`sed -n '712,779p' CONTEXT.md` (Override).

**Implement:** `AbsenceRule.Derive`, `TagDeclaredRule.Derive`.

**Tests (inventory § Derived-obligation rules, 16):**
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
- **a moved instance does not**
- three contiguous absences derive **one** obligation, due before the first
- a derived Task is never `Unprocessed` and never `Stale`
- a derived Task cannot be postponed

The last two constrain `StatusRules` (Task 5's file). If they cannot pass without a change there,
**report it — do not edit `Status.cs`.**

---

# Wave 4 — consumes wave 3

## Task 12: Ranking

**Files:** `src/TaskGuide.Domain/Ranking/Ranking.cs`, new
`tests/TaskGuide.Domain.Tests/RankingTests.cs`.

**Read:** `sed -n '1714,1738p' CONTEXT.md` (Urgency band), `sed -n '1739,1750p' CONTEXT.md`
(Duration as tiebreak), `sed -n '1751,1759p' CONTEXT.md` (Opportunities),
`sed -n '1760,1861p' CONTEXT.md` (Scarcity), `sed -n '141,156p' CONTEXT.md` (Deadline).
**ADR-0004 is binding here.**

**Implement:** `RankKey.CompareTo`, `Ranker.Rank`.

**Tests (inventory § Ranking, 7):**
- the sort is total: no two Tasks compare equal unless every key ties
- band 1 (passed) outranks band 2 (within horizon) outranks band 3 (no pressure)
- **within a band, fewest Opportunities first**
- on an Opportunities tie, longest Duration first
- on a Duration tie, oldest `CreatedAt` first
- a Task with a Deadline does not automatically outrank one without — bands, not a continuous key
- inside band 2, a sooner Deadline yields a shorter horizon and therefore a higher rank, without a
  deadline key being applied on top

Four derived keys, no priority field, no tunable weights (ADR-0004).

## Task 13: Orphan detection

**Files:** `src/TaskGuide.Domain/Ranking/Opportunities.cs` (the `OrphanDetection` half — Task 9
is finished by now), new `tests/TaskGuide.Domain.Tests/OrphanDetectionTests.cs`.

**Read:** `sed -n '1760,1861p' CONTEXT.md` (Scarcity), `sed -n '1751,1759p' CONTEXT.md`
(Opportunities), `sed -n '67,140p' CONTEXT.md` (Status). ADR-0004.

**Implement:** `OrphanDetection.IsOrphan` and the `ZeroKind` discrimination.

**Tests (inventory § Orphan detection, 10):**
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

# ADR-0005 — One ~30 s tick recomputing predicates; no timers, no catch-up sweep

**Status:** Accepted · **Source:** [#16](https://github.com/jpjerkins/task-guide/issues/16)

## Context

Reminders fire when an Availability Window opens. The naive designs are per-fire timers or a
scheduler plus a startup catch-up sweep. Both keep state that has to be rebuilt on restart, and both
produce a *second* implementation of the missed-fire policy that can drift from the first.

## Decision

**One loop, recomputing on a ~30-second tick.**

```
every ~30s:
  shape = Override[today] ?? Pattern[weekday]
  for each Window in shape:
      due?      start <= now
      alive?    now < end
      unfired?  no Fire record for (today, windowId, kind)
      → match, and push if the result is non-empty
  for each pending Snooze where dueAt <= now:
      → re-match, push, record
  fallback: runway day && uncarried && no chance left && now >= 11:00a
  retention sweep: unlink fire files older than 30 days   (unguarded, every tick)
```

Every rule is a **predicate about a moment**, not an event on a calendar. Recomputation is therefore
the natural shape, not an implementation convenience. What it buys:

- **Downtime is indistinguishable from a slow tick**, so the missed-fire policy *is* the normal path.
- **Nothing to rebuild on restart** — no timer state, no in-memory snooze chain.
- **DST-safe**, because instants are re-derived from clock times each tick.
- **A pending Snooze is just another row** the same loop picks up.

### Fire record

`/data/fires/<date>.json`, keyed `(date, windowId, kind)` where kind is
`window | unconditional | snooze | fallback`. **Fires and pending Snoozes are the same structure** —
a fire has a `firedAt`; a pending Snooze has a future `dueAt` and a null `firedAt`.

`unfired?` = no row with a `firedAt` for that key. **That is the engine's whole idempotency
guarantee.** The row denormalises `windowName` / `windowStart` / `windowEnd` — it records the Window
**as it was when it fired**, which an id could never do honestly.

### DST

Clock times are authored; instants are derived per date.

| Case | Resolution |
|---|---|
| Ambiguous start (fall back) | the **first** occurrence — a Window fires at its start |
| Nonexistent start (spring forward) | **clamp** to the first valid instant (the gap's end) |
| Span crossing a transition | measured between instants → honestly an hour shorter or longer |

A Window lying entirely inside the spring gap clamps to zero length, and a zero-length Window is no
opportunity, so it does not fire. The degenerate case handles itself.

### Delivery is at-least-once

**`firedAt` is written only when Pushover accepts the message.** A failed push reads as unfired next
tick and is retried — bounded by rules already in force: an opportunity stops retrying when its span
closes, an obligation at the Day boundary. **Retry is the ordinary path, not a retry subsystem.**

Accepted risk: Pushover accepts but the response is lost → one duplicate push. Chosen deliberately
to protect the silence biconditional.

### The silence guarantee

> **`no push ⟺ nothing fit`**, enforced in one place: the matcher.

Two exceptions, both **obligations** — the biconditional is a claim about *opportunity*: an
unconditional fire with no matches (the Event becomes the title), and the 11:00a fallback push on a
windowless runway day.

### `ttl`

Derived from the same boundary that governs the fire: a Window fire and an in-span snooze re-fire run
to the Window's end; a past-span re-fire, an unconditional fire and a fallback push run to the Day
boundary.

## What this forbids

- **No timers, no scheduled jobs, no startup catch-up sweep.** A catch-up path is a second
  implementation of the missed-fire policy.
- **No daily cap or push budget.** Any cap weakens the biconditional to
  `no push ⟺ nothing fit OR budget spent`, at which point a quiet afternoon is unreadable. Volume is
  self-limiting: pushes are bounded by windows the user authored, and the honest fix for too many is
  editing the schedule.
- **Do not suppress duplicate shortlists across Windows.** Two Windows are two genuinely separate
  chances.
- **Do not store fires on the date's Override.** That forces an Override into existence for every day
  that fires anything, turning the sparse-Override design into a fully reified calendar.
- **Do not freeze a Window to a UTC instant at authoring.** Every Window would silently shift an hour
  twice a year.
- **Do not guard the retention sweep** with a date-changed flag. It writes nothing on a normal tick
  and only unlinks whole day-files; a guard avoids a 31-entry directory listing off the page cache
  and costs a field, a restart behaviour and a paragraph.
- **An Override's copy must preserve each Window's id.** Copying is not minting. Otherwise an
  already-fired Window reads as unfired and pushes again a minute later.

## Liveness: the staleness threshold is 90 s

`HealthReporter.StalenessThreshold` is **90 seconds — three missed ticks at the ~30 s cadence.**
Approved by the user 2026-08-27; it is a decision, not an inherited default.

It is the line between *a slow tick* and *the loop is dead*, so it inherits this ADR's central
property: **downtime is indistinguishable from a slow tick**, and the threshold is what decides how
much of that indistinguishability `/health` is willing to absorb before it stops absorbing. Three
ticks is tolerant of one lost tick and one slow one, and still names a stall inside two minutes.

**If the tick interval changes, this changes with it.** The constant is 3× the cadence, not an
independent number — decoupling them would let a slower tick read as permanently unhealthy.

## Known scaffolding to remove

The walking skeleton's tick pushes only once and only when a real Task exists, gated by a one-push
flag (`TickLoop`, an `Interlocked` 0/1). **That flag is scaffolding and dies with this ADR's
implementation** — it must not be inherited, and neither must the push-only-once-a-real-Task-exists
behaviour it gates. Both describe the skeleton, and the engine above replaces them wholesale.

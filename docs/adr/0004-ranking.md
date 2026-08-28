# ADR-0004 — Ranking is four derived keys; there is no priority field

**Status:** Accepted · **Source:** [#11](https://github.com/jpjerkins/task-guide/issues/11), [#18](https://github.com/jpjerkins/task-guide/issues/18)

## Context

After Dimension filtering, eligible Tasks must be ordered. The governing idea is **spend the rarest
opportunity**: a chance that only fits one thing should go to that thing.

## Decision

**Four keys, in order. The sort is total, and every step is derived from data already on screen —
so any Task's position is explainable in a sentence.**

1. **Urgency band** — bucketed, three values
2. **Scarcity** — fewest **Opportunities** first
3. **Longest Duration first**
4. **Oldest `CreatedAt`** — backstop, reached only on an exact three-way tie

### Urgency band

| Band | Condition |
|---|---|
| 1 | Deadline **passed** |
| 2 | Deadline **within the horizon** (it clipped the rolling 7 days) |
| 3 | **No deadline pressure** — no Deadline, or one beyond the horizon |

**The bands invent no thresholds.** They are exactly the three cases the Opportunity horizon rule
already distinguishes, so there is nothing to tune and nothing that can drift out of sync with it.
Inside band 2, a sooner Deadline yields a shorter horizon, hence fewer admitting Windows, hence a
higher rank — **Deadline order largely falls out of Scarcity** rather than being imposed on top.

### Opportunities and the horizon

**Opportunities** = how many Availability Windows *ahead of now* would admit this Task. A plain
count, computed live on read, **never cached and never stored**.

The horizon is a **rolling `min(7 days, time to Deadline)` measured from now** — *not* the Pattern's
Sun–Sat week. Without a Deadline it is a true rolling 7 × 24h. With a Deadline ahead it runs to the
end of that day. With a Deadline **already passed the bound is dropped** and it reverts to the plain
rolling 7 days — otherwise the horizon goes negative and every overdue Task misreports as an Orphan.

Rolling matters because **computing it means walking real dates, which applies Overrides and
Events.** A Pattern-week count is structurally blind to the fact that *this* week is unusual.

### Duration as tiebreak

> **The biggest Task that fits leads.**

Shortest-first is the exact inversion: it spends long Windows on Tasks any Window could have taken,
and long Tasks starve. This is **not a minor slot** — three bands and a small-integer Opportunity
count mean ties are routine, so this key often chooses what is visible.

### Two kinds of zero

Distinguished by *also* computing the Pattern-week count:

- **Orphan Task** — no Window in the active Pattern can *ever* admit it. Something is malformed.
  Categorically worse than `Unprocessed` or `Stale`, so it gets a badge/filter, not just a count.
- **None in this stretch** — normally doable, every admitting Window merely outside the horizon.
  Nothing is wrong.

**Orphan detection respects the Status gate and ignores the clock gates.** Only an `Active` Task can
be an Orphan. **Defer** and **Postpone** are deliberately *not* consulted — orphan-ness asks whether
any Window could *ever* admit the Task. That asymmetry is the point: *Status means intent; Defer is
a clock fact.*

## What this forbids

- **No priority or importance field, ever.** Self-assigned priorities rot — everything drifts to
  High — and a field that only works if groomed will be wrong.
- **No weighted score** combining band and Scarcity. The weights would have no principled value and
  *"why did this rank first"* becomes unanswerable. This project rejects knobs.
- **No continuous deadline key.** Any Task with a Deadline would outrank every Task without one, and
  exact dates rarely tie, so Scarcity would never get to speak.
- **Age is not a ranking *penalty*.** The `Stale` gate already encodes that judgement; applying it
  twice would double-count *and* be self-fulfilling — an aging Task surfaces less, so gets done
  less, so ages out. Age is only the final tiebreak, oldest first, which carries none of that.
- **Do not cache Opportunities or Orphan-ness.** Both move with the clock, Windows, Day templates,
  Overrides, Events and the active Pattern.

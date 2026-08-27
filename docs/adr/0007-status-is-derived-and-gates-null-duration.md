# ADR-0007 — Status is derived, and the Status gate is what handles a null Duration

**Status:** Accepted · **Source:** [#47](https://github.com/jpjerkins/task-guide/issues/47), Status entry in `CONTEXT.md`

## Context

`Duration` is nullable. It is nullable **on purpose**: quick capture from a phone supplies a title
and nothing else, and *a Task with no Duration* is precisely the definition of `Unprocessed`. This
is load-bearing domain meaning, not a defect to be defended against.

The trap is that a coding agent meeting `int? Duration` will reach for one of two reflexes, and
**both are wrong, in opposite directions** — the model records this failure as already made once:

- treat a missing Duration as matching **nothing** → every unprocessed Task reports 0 Opportunities
  and is flagged a **false Orphan**
- treat it as matching **everything** → the count is confident, and flips the moment a Duration arrives

## Decision

**Status is a derived label, not a stored field.** Exactly one fact behind it is authored —
**completion**. `tasks.json` carries no `status`.

| Label | Derived from | |
|---|---|---|
| `Done` | a completion entry covering the current instance | the only authored fact |
| `Unprocessed` | the Task has **no Duration** | disables marking off — nothing yet to be done *within* |
| `Stale` | the age / missed-instance rules | read off `CreatedAt` and the completion log |
| `Active` | none of the above holds | the residue — never set, never displayed as a chip |

**The labels are ordered and the order is load-bearing. First match wins, top to bottom.** Derived
facts co-occur where typed values could not — a Task with no Duration can equally be 59 days old.
Exclusivity comes back as a rule rather than as a type, which three settled things need: the
reminder footer's two counts read as a partition, an Orphan count is a **third, disjoint** number,
and *"comparing categories presupposes a Task is only ever in one of them."*

### The consequence for null Duration

**`Status` is an eligibility gate: only `Active` Tasks are matched.** An `Active` Task by definition
carries a Duration. Therefore:

> **Matching, Ranking, Scarcity, Opportunities and Orphan detection never see a null Duration.**
> They sit behind the gate. They take a non-null Duration and need no null handling at all.

For an `Unprocessed` Task, orphan-ness is not false but **undefined** — matching has exactly two
inputs, the Task's Tags and its Duration, and `Unprocessed` *is* the absence of the second. A
Pattern-week count over it is computed from a missing operand. Supply the Duration and the question
becomes meaningful.

## What this forbids

- **Do not store `status`.** It was removed from `tasks.json`, not migrated, because nothing read it.
- **Do not null-guard Duration inside the matching, ranking or orphan-detection code.** A null there
  means the Status gate was bypassed — that is a bug to surface, not to absorb. Model it as
  non-nullable at the boundary where the gate is applied.
- **Do not default a null Duration to `0`, to a sentinel, or to "matches everything".** Both
  reflexes are named above and both are rejected.
- **Do not add an `Unprocessed` flag, a `Deferred` status, or an authored archive bit.** Each is a
  second, weaker way to say what an existing fact already says.
- **Do not let an `Unprocessed` or `Stale` Task also be an Orphan.** Orphan-ness is a claim about an
  `Active` Task only.

## Consequence for the UI

`Unprocessed` is an **exception, not a backlog** — the normal capture path does ask for Duration.
The repair surface is the five Duration buttons on the landing page.

**What an absent Duration should look like in a list row is still an open product question.** The
SPA currently renders no duration pill and no placeholder glyph, which is a deliberate
non-invention, not a decision. Settle it before the task-list surface is built out.

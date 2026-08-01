# CONTEXT — task-guide

Ubiquitous language for `task-guide`, a single-user opportunistic task reminder system.

Decisions recorded here were settled in [Task property model](https://github.com/jpjerkins/task-guide/issues/2).
The effort's map is [Map: task-guide](https://github.com/jpjerkins/task-guide/issues/1).

## Glossary

### Task

A thing to do. Carries a small set of typed fields plus **Tag** values across **Dimensions**.

| Property | Type | Required | Notes |
|---|---|---|---|
| Title | text | yes | |
| Notes | free text | no | A URL or a sentence of context, so it doesn't get crammed into the Title |
| Duration | bucket — `2` / `10` / `30` / `60` / `Longer` (minutes) | to be `Active` | The one property with no safe default |
| Status | `Unprocessed` \| `Active` \| `Done` \| `Stale` | yes | Typed field, never a Tag |
| Tags | dimension-qualified values | no | See **Tag** |
| Deadline | date | no | Ranking input, not a Dimension |
| Defer | offset relative to Deadline | no | Earliest surface time; detail scoped to issue #10 |
| Recurrence | — | no | Detail scoped to issue #10 |
| CreatedAt | timestamp | yes | Age drives the `Stale` gate — **not** ranking |

### Status

Exclusive, typed, and an eligibility gate: **only `Active` tasks are matched.**

- **`Unprocessed`** — captured without the information needed to match, e.g. a capture path that
  couldn't ask for Duration. The normal capture path does ask, so this is an exception, not a backlog.
- **`Active`** — matchable.
- **`Done`** — completed. Tasks are marked off deliberately and separately from any reminder.
- **`Stale`** — aged past a threshold without being done. Hard-excluded from matching, like
  `Unprocessed`. Staleness is evidence the task is badly formed; the response is to **reword, slice
  smaller, or delete** it.

`Unprocessed` and `Stale` counts are surfaced as a **footer count** on reminders
("6 to process, 3 stale"). That footer is the only nudge — there is no dedicated notification for
either pile.

### Dimension

**An axis that both a Task and an Availability Window carry a value on.** Dimensions are the only
thing matching looks at.

Each Dimension declares, in code:

- its **value type** — categorical (set membership) or ordinal (comparison),
- its **default**, applied when the Task carries no value on that axis,
- its **matching rule**.

There is deliberately **no UI for managing Dimensions or their defaults**. Adding one is a code change.

Known Dimensions (illustrative — the registry is authoritative):

| Dimension | Value type | Default |
|---|---|---|
| Location | categorical | home |
| With whom | categorical | nobody |
| Mental energy | ordinal | low |
| Weather | categorical | any |
| Duration | ordinal | — (required) |

Defaults are **per-Dimension**, not global. A blanket "absent means unconstrained" or "absent means
default" rule gets some axes wrong. Every default is chosen to be the *least constrained, most common*
case, so an untagged Task correctly lands in ordinary at-home windows — that is why the common case
carries zero Tags.

**Not Dimensions:** Deadline, Defer, Recurrence, Status, age. A Window has no value on those axes,
so there is nothing to compare against.

### Tag

A **value belonging to exactly one Dimension**. Tags are stored **dimension-qualified**, not as bare
strings, so that absence is explicit per Dimension and two Dimensions cannot collide on a name. Entry
may be loose (type `#sam`); the Dimension registry resolves it on write.

**Inert Tag** — a Tag that resolves to no Dimension. It is **kept but inert**: stored, visible in the
UI, ignored by matching. This is the intended path for a new idea — invent the Tag now, add the rule
in code later. Inert Tags act as a staging area for future functionality.

### Matching rule

One per Dimension, evaluated against a Window. The common shape is the **gated boost**:

> Window carries the value → **include the Task and rank it higher**
> Window lacks the value → **exclude the Task**

Rules are per-Dimension only; they do not read other Dimensions' values.

### Derived-obligation rule

A rule that reads a Tag and produces a **new obligation with its own deadline** — e.g. a Task tagged
with a person may derive "have them ask off work before the preceding Friday". Neither filter nor
rank; a third mechanism. Scope and design are open (see the derived-obligation ticket on the map).

### Rules generally

Both rule kinds live **in code**, written with the open/closed principle in mind, with **no management
UI**. New behaviour arrives as a new rule, not as configuration.

## Ranking

After Dimension filtering, eligible Tasks are ranked by:

1. **Deadline urgency**
2. **Dimension boost** — how many Dimensions the Window positively matched
3. Tiebreak

There is deliberately **no priority/importance field**. Self-assigned priorities rot — everything
drifts to High — and a field that only works if it is groomed will be wrong.

**Age is not a ranking input.** Older Tasks are lower-value, but the `Stale` gate already encodes that
judgement. Applying it a second time as a ranking penalty would double-count *and* be self-fulfilling:
an aging Task would surface less, so get done less, so age out — the penalty would cause the rot it
claims to measure. Below the threshold, a Task competes on its merits.

## Capture

**Duration is the only property the capture path must supply.** Everything else has a safe default.

Two iOS Shortcuts rather than one branching Shortcut, so the fast path is never taxed by a question
that is almost always answered "no":

- **Quick add** — Title, then the five Duration buckets. Two interactions; the Task is `Active`.
- **Add task with details** — Title, Duration, mental energy, deadline. Also lands `Active` —
  taking the time to enter details *is* processing.

Both write the **same** Task through the same endpoint; the details path simply sends more fields.

A notification is a **doorbell carrying one URL** — Pushover has no actionable notifications
(see issue #3). Any inline triage ("one unprocessed Task + five Duration buttons") therefore lives on
the **landing page** the notification opens, not in the notification itself.

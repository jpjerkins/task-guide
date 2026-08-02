# CONTEXT — task-guide

Ubiquitous language for `task-guide`, a single-user opportunistic task reminder system.

Decisions recorded here were settled in [Task property model](https://github.com/jpjerkins/task-guide/issues/2),
[Weekly schedule and availability window model](https://github.com/jpjerkins/task-guide/issues/7),
[Override / dated-event model](https://github.com/jpjerkins/task-guide/issues/8),
and [UI scope and screens](https://github.com/jpjerkins/task-guide/issues/12) — which, in the course of
prototyping, revised the Dimension, Tag, Matching rule and Ranking entries.
The effort's map is [Map: task-guide](https://github.com/jpjerkins/task-guide/issues/1).

## Glossary

### Task

A thing to do. Carries a small set of typed fields plus **Tag** values across **Dimensions**.

| Property | Type | Required | Notes |
|---|---|---|---|
| Title | text | yes | |
| Notes | free text | no | A URL or a sentence of context, so it doesn't get crammed into the Title |
| Duration | bucket — `2` / `10` / `30` / `60` / `Longer` (minutes) | to be `Active` | An **ordinal Dimension**, not a plain field. The one property with no safe default |
| Status | `Unprocessed` \| `Active` \| `Done` \| `Stale` | yes | Typed field, never a Tag |
| Tags | dimension-qualified values; a categorical axis may carry **several** | no | See **Tag** |
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

**Dimensions do not all work the same way.** There are **two algebras**, and which one a Dimension uses
determines everything about how it is declared, stored and matched.

#### Categorical Dimensions — subset

Location, With whom, Weather. Both sides carry a **set** of values.

> **Window values OR together. Task values AND together.**
> A Task fits when its set on that axis is a **subset** of the Window's.

| Task | Window | Fits? | Why |
|---|---|---|---|
| `{}` | `{}` | yes | ∅ ⊆ ∅ — the untagged Task in an unremarkable Window |
| `{}` | `{garage}` | yes | an untagged Task still fits a garage Window |
| `{garage}` | `{}` | **no** | the Window said nothing about location |
| `{garage}` | `{garage, outside}` | yes | the Window ORs |
| `{Sam, Ana}` | `{Sam}` | **no** | the Task ANDs — Ana isn't there |
| `{Sam, Ana}` | `{Sam, Ana, the kids}` | yes | both required people are present |

**Categorical Dimensions declare no defaults on either side.** Absence is the empty set, and the empty
set already does everything a default was doing: ∅ is a subset of everything, so an untagged Task fits
everywhere, and a Window that declares nothing admits only untagged Tasks. An earlier version of this
document gave every categorical axis a task-side and window-side default (`Location: home`); no case
exists where those defaults change an outcome versus absence.

#### Ordinal Dimensions — ceiling

Mental energy, Duration. Each side carries **one** value, and the Window's is a ceiling.

> **Task value ≤ Window value → fits.**

Ordinal Dimensions **do** declare a task-side and a window-side default, applied when that side is
silent. Both are the least-constrained common case.

**Duration is an ordinal Dimension, not a special field.** Its window-side value is **derived from the
Window's length** — a 45-minute Window admits the 30-minute bucket and below. Nothing to declare,
nothing to keep in sync, and it can never contradict the clock.

Each Dimension declares, in code: its **algebra** (categorical or ordinal), its **value set**, and —
for ordinal axes only — its **two defaults**. There is deliberately **no UI for managing Dimensions**.
Adding one is a code change.

Known Dimensions (illustrative — the registry is authoritative):

| Dimension | Algebra | Defaults |
|---|---|---|
| Location | categorical — subset | none (absence = ∅) |
| With whom | categorical — subset | none (absence = ∅) |
| Weather | categorical — subset | none (absence = ∅) |
| Mental energy | ordinal — ceiling | task `low`, window `low` |
| Duration | ordinal — ceiling | task: required; window: derived from length |

**Not Dimensions:** Deadline, Defer, Recurrence, Status, age. A Window has no value on those axes,
so there is nothing to compare against.

**The drift is always toward orphans.** A tagged Task is doable *only* if some Window declares that
tag. Tags are cheap to add to a Task and Windows are edited rarely, so tagging outruns declaring —
and switching the active Pattern can orphan a whole class of Tasks at once. See **Scarcity**.

### Tag

A **value belonging to exactly one Dimension**. Tags are stored **dimension-qualified**, not as bare
strings, so that absence is explicit per Dimension and two Dimensions cannot collide on a name. Entry
may be loose (type `#sam`); the Dimension registry resolves it on write.

A Task or Window may carry **several Tags on one categorical Dimension**, and the two sides mean
different things: on a Task they **AND** (all must be satisfied), on a Window they **OR** (any is
enough). See **Dimension**. Ordinal axes carry exactly one value per side.

**Inert Tag** — a Tag that resolves to no Dimension. It is **kept but inert**: stored, visible in the
UI, ignored by matching. This is the intended path for a new idea — invent the Tag now, add the rule
in code later. Inert Tags act as a staging area for future functionality.

### Availability Window

A **clock-bounded span within a day**, carrying a name and a set of Dimension values. The day's
Windows are what drive reminders: **a Window firing *is* the reminder**, and its Dimension values are
the filter selecting matching Tasks. There is no separate reminder-definition concept.

| Property | Type | Notes |
|---|---|---|
| Name | text | Label and reminder copy — "Evening prep". Not an identity |
| Start / End | clock times | Bounded span. Duration is real arithmetic, not a vibe |
| Dimension values | partial | Declares what it declares; unset axes take the window-side default |

A Window is a **per-day instance**, not a shared definition. "Evening" on Tuesday and "Evening" on
Saturday are two different Windows that happen to share a label and may have very different spans —
volleyball-season evenings are short. Editing one never propagates to the other.

### Day template

A **named, reusable day shape** — "Ordinary weekday", "Volleyball Tuesday", "Tournament Saturday",
"Travel day". It holds a set of Availability Windows and, optionally, **Event prototypes**. The unit
of substitution: swapping one day of a week means pointing that day at a different Day template.

**Event prototype** — a dateless Event (name + clock times) held by the template, instantiating into
a real dated **Event** when the template is applied. This is the same relationship a Window already
has: a Window inside a template is dateless and becomes a per-day instance on application. Prototypes
let a template record *why* it has its shape — "Band concert night" ends karate at 6:30 because the
concert prototype sits at 7:00 — instead of carrying an unexplained hole. Instantiated Events detach
from the prototype, so renaming or moving one edits that date only.

A Day template is used **two different ways**, and the difference is load-bearing:

- **By reference**, in a **Pattern** — the Pattern stores a pointer; edits propagate.
- **By value**, in an **Override** — the date stores copied Windows; edits never reach it.

### Pattern

**Seven Day template references**, one per weekday. Exactly one Pattern is active at a time; a
seasonal change (children's sports) is a switch of the active Pattern.

References are **live** — editing a Day template propagates to every Pattern using it, and that
propagation is the point. The risk is handled by visibility, not by prevention: **editing a Day
template shows its usage list** ("used by 3 Patterns: Volleyball, Summer, School year") before saving.
To diverge, clone the template under a new name. There is no versioning and no copy-on-assign.

**A Pattern is an assumption, not a calendar.** It is never reified into dated records. Any future
date's shape is *computed* on demand from the active Pattern; the only stored dated records are the
sparse **Overrides** and **Events** layered on top.

A Day template carrying an Event prototype **may** be assigned to a Pattern slot, which generates a
weekly recurring Event. This is deliberately **not blocked** — several ordinary weekly commitments
(church, karate) are event-shaped, and a validation rule would foreclose an experiment worth running.
See **Event** for how recurring Events surface differently from dated ones.

### Override

**A single date whose day shape differs from what the active Pattern assumes.**

> **A Pattern describes the assumption; an Override describes reality.**
> Editing an assumption must never retroactively rewrite a reality already recorded.

- **The date is the unit.** There is no multi-day Override object — "college kid home for the
  weekend" is *two* dated Overrides created in one authoring gesture. A date has exactly one shape,
  so **conflicts are unrepresentable rather than resolved**: no nesting, no precedence rule, no
  "what wins" edge case. Applying an Override to an already-overridden date is a replacement, and
  the UI confirms before clobbering.
- **An Override always stores copied Windows, never a reference.** Applying a named Day template is
  a **stamp**, not a link — it lays the shape down and the connection ends there. Subsequent edits
  to that template do not reach the date, and edits to the date do not reach the template or any
  other date using it.
- **Per-day variation falls out free.** Each date in a span names its own Day template or its own
  one-off day; nothing has to express "different on day 3".

**One-off day** — an Override whose Windows belong to that date alone, with no named template behind
them. Created when an **Event** overlaps an existing Window (see below), or by editing a stamped date
directly. A one-off day may be **promoted** to a named Day template afterwards; promotion copies the
shape *outward* and the source date keeps its own copy — it does **not** re-link, because that would
put a hand-tailored date back under live propagation as a silent side effect of naming something.

Rejected alternatives: a **live diff** ("Ordinary weekday, minus the 6–10pm window") survives template
edits but becomes nonsense once the referenced Window changes shape, and debugging a wrong reminder
means replaying a patch mentally. **Copy-on-write** (live until first edit) recovers "fix the
template, unedited future stamps update", at the price of a date's shape changing while it still
*looks* assigned, plus a storage model persisting two states of the same thing.

### Event

**A dated, clock-timed thing the user must attend** — a band concert, a trip departure. Not a Task
and not an Availability Window.

An Event cannot be modelled as a Window: a Window fires only when Tasks match it, so an obligation
expressed as a Window would be silently swallowed by the restraint mechanism. It cannot be modelled
as a Task either — every Task in this system is *opportunistic*, and a concert is the opposite:
fixed time, single obligation, must not be missed.

| Property | Type | Notes |
|---|---|---|
| Name | text | "Band concert" |
| Date | date | Instantiated from a prototype, or entered directly |
| Start / End | clock times | Bounded span, used for Window overlap resolution |
| Tags | dimension-qualified | Same mechanism as a Task's — registry resolution on write, unresolved Tags kept but inert |

**Overlap resolution.** Creating an Event that overlaps an existing Window — *even partially* —
prompts for how to handle that Window: **replace** it, **truncate** it (at either end; truncating the
start moves the fire time, truncating the end does not), or **split** it around the Event. The result
is a one-off day for that date. So an Event *generates* an Override — one user action, one question,
two artifacts.

**Surfacing.** An Event has **no notification of its own**. It appears as a brief note plus weekday
(**no time** — the weekday is what makes it stick) in the reminder footer, alongside the
`Unprocessed`/`Stale` counts. Riding an existing channel means an Event costs zero additional pushes.

- **Dated Event** — footer for the **3 days up to and including the day**, and during that span the
  **first Availability Window of each day fires unconditionally**, matching Tasks or not. That is the
  guaranteed carrier: without it, three quiet days would surface the obligation nowhere at all.
  Later Windows keep the normal rule, so a busy day surfaces the Event repeatedly and naturally.
- **Recurring Event** (instantiated from a prototype in a Pattern slot) — footer **on its own day
  only**, with **no unconditional firing**. The 3-day runway exists for the irregular thing that would
  otherwise be forgotten, which a weekly commitment is not. Without this distinction, one weekly
  prototype would put every day within 3 days of an Event and silently convert the whole system to
  always-fire.

Rejected: a dedicated lead-time notification per Event — an extra push for something the footer
already carries. Rejected: firing *every* Window during the 3-day span — up to a dozen Task-less
pushes per Event, which trains the footer to be swiped away unread.

### Firing

**Every Availability Window fires at its start time.** There is no per-Window notify flag and no
day-level notification budget.

Notification restraint comes from a different lever: **if no Tasks match, no notification fires.**
Silence is therefore always truthful — no push means nothing fit. This decouples restraint from
authoring fidelity, so the week can be authored honestly without deleting Windows to quiet the phone
(which would also destroy a matchable moment).

**"Silence is truthful" is a claim about opportunity, not obligation.** Windows offer opportunities
and stay silent when nothing fits; **Events** are obligations and always speak. The one exception to
the rule above follows from that: during the 3 days before a **dated Event**, the day's *first* Window
fires whether or not Tasks match, because it is carrying the Event's footer. See **Event**.

A Window is **not** re-fired automatically part-way through, however long it is. A second push
carrying no new information is noise, and it would destroy the truthfulness of silence.

### Snooze

The **only** re-fire path, always user-initiated from the landing page a notification opens.

- **Interval** — `clamp(25% of Window duration, 5 minutes, 30 minutes)`. Proportional, floored so a
  short Window cannot buzz again almost immediately, capped at 30 minutes.
- **Repeats** — unlimited, same interval each time. No escalation, no cap.
- **Expiry** — the reminder dies at end of day. Snooze means "show me this again *later today*".
- **Past Window end** — allowed. Work sometimes continues past the authored span.
- **Content** — **re-matched at the re-fire time against the original Window's Dimension values, using
  current Task state.** Same filter, fresh list: Tasks completed since the first fire drop off, newly
  captured Tasks that fit appear. Snoozing while actively working a long list is the motivating case.
  The reminder keeps its Window's name throughout.

Re-matching against *whatever Window is live* at the re-fire time is explicitly rejected: it silently
changes the filter, has no answer when no Window is live, and is really "show me what fits now" — a
separate on-demand feature of the UI.

### Matching rule

A Dimension's matching rule is fixed by its **algebra**, not written per Dimension: categorical axes
compare by **subset**, ordinal axes by **ceiling**. See **Dimension** for both, with worked cases.

Rules are per-Dimension only; they do not read other Dimensions' values. A Task must satisfy **every**
Dimension to be eligible — matching is a conjunction across axes, whatever each axis's algebra.

An earlier version of this document described a single **gated boost** shape ("Window carries the value
→ include and rank higher; Window lacks it → exclude"). The filtering half of that is what the subset
rule now says precisely; the ranking half is replaced by **Scarcity**.

### Derived-obligation rule

A rule that reads a Tag and produces a **new obligation with its own deadline** — e.g. something
tagged with a person may derive "have them ask off work before the preceding Friday". Neither filter
nor rank; a third mechanism. Scope and design are open (see the derived-obligation ticket on the map).

**Events** now carry Tags too, so they are candidate triggers alongside Tasks. Note the asymmetry the
ticket must resolve: a derived obligation needs **a date to count back from**, which an Event always
has and a Task has only when it carries a Deadline. What a rule *produces* can be an ordinary **Task
with a deadline**, re-entering the system through normal matching and ranking — no third notification
path.

### Rules generally

Both rule kinds live **in code**, written with the open/closed principle in mind, with **no management
UI**. New behaviour arrives as a new rule, not as configuration.

## Ranking

After Dimension filtering, eligible Tasks are ranked by:

1. **Deadline urgency**
2. **Scarcity** — see below
3. Tiebreak

There is deliberately **no priority/importance field**. Self-assigned priorities rot — everything
drifts to High — and a field that only works if it is groomed will be wrong.

### Scarcity

> **How many Availability Windows *ahead of now* would admit this Task. Fewest first.**

The governing idea is **spend the rarest opportunity**. A Task that only one Window can accommodate
should outrank one that fits eighteen — you will get another chance at the second today.

Scarcity replaces an earlier rule that ranked by *how many Dimensions the Window positively matched*,
which counted matches without ever justifying why a count should mean importance. Scarcity also
**unifies the two algebras**: long Windows, high-energy Windows and oddly-located Windows are all
simply rare, so nothing needs special-casing per axis.

#### The horizon

**A rolling `min(7 days, time to Deadline)`, measured from now** — *not* the Pattern's Sun–Sat week.

- Without a Deadline it is a true rolling 7 × 24h, running to the same clock time eight days out.
  A once-a-week opportunity is therefore counted **exactly once** whatever hour you ask.
- With a Deadline it runs to the end of that day, so the number reads *"3 chances before it is due"*.

Two reasons for rolling rather than a fixed week. The small one: a fixed week is a lie by Friday
afternoon, when half its Windows are already spent. The large one: **computing a rolling horizon means
walking real dates, which applies Overrides and Events.** A Pattern is deliberately never reified into
a calendar, so a Pattern-week count is structurally blind to the fact that *this* week is unusual —
a travel-day Override can remove the only Window that admits a Task, and a Pattern-week count would
still confidently report "1 this week" and rank that Task first.

Scarcity therefore moves with the clock as well as with Windows, Day templates, Overrides, Events and
the active Pattern.

#### Two kinds of zero

They look identical to the user and mean opposite things, so they are distinguished by also computing
the Pattern-week count:

- **Orphan Task** — no Window in the active Pattern can *ever* admit it. Something is malformed:
  either the Task carries a Tag nothing declares, or the schedule is missing a Window. Orphans surface
  as a count alongside `Unprocessed` and `Stale`, and are the only defence against the tagging drift
  described under **Dimension**.
- **None in this stretch** — normally doable, but every admitting Window falls outside the horizon,
  usually because an Override or Event displaced it. **Nothing is wrong**; the Task should simply not
  be ranked as though this were its big chance.

> **Open:** with the horizon now Deadline-bounded, scarcity has absorbed much of what *Deadline
> urgency* was doing as the primary sort key. Whether the two collapse into a single key is left to
> the matching-and-selection ticket, which owns ranking. Both are still listed above.

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

# ADR-0006 — A system-presented control must survive its own input events

**Status:** Accepted · **Source:** [#46](https://github.com/jpjerkins/task-guide/issues/46), [#48](https://github.com/jpjerkins/task-guide/issues/48), carried by [#41](https://github.com/jpjerkins/task-guide/issues/41) · **Empirically settled** 2026-08-24

## Context

A native date picker on iOS Safari was observed dismissing itself mid-interaction. A seven-row probe
was run on-device, on a plain page and then again inside a sandboxed cross-origin iframe, with every
handler verified byte-identical **by diff, not by eye**.

| # | Row | Plain page | Sandboxed iframe |
|---|---|---|---|
| 1 | Bare input, zero JS | stayed | **stayed** |
| 2 | Passive `input` listener | stayed | **stayed** |
| 3 | `innerHTML` rebuild | **dismissed** | **dismissed** |
| 4 | Value written back, same node | stayed | **stayed** |
| 5 | Node replaced | **dismissed** | **dismissed** |
| 6 | `transform` ancestor | stayed | **stayed** |
| 7 | Focus-stealing | stayed | **stayed** |

**The remount is the sole cause, and it is container-independent.** No row differed between
containers, so there is no interaction effect either. The iframe sandbox was a red herring — and
`task-guide` never runs in an iframe anyway (ADR-0002).

## Decision

> **A system-presented control must survive its own input events.**

**Never remount a control in response to its own change.** Concretely, in a handler for a control's
own `input`/`change`:

- **no changing `key`**
- **no conditional-render branch swap** that replaces the node
- **no `innerHTML` rebuild of the control or any ancestor**

**Reassigning `value` on the same node is fine** (row 4).

## Scope — deliberately not date-specific

This is stated generally on purpose. `<select>`, `type="time"`, `type="month"`, the
ordinal-Dimension sliders and the Recurrence editor all couple the same way. It applies to **every**
date-entry surface: Deadline, Defer's absolute form, Postpone's escape, Recurrence's first-due, an
Event's date, and the Override rail's *"Pick a date…"*.

## Consequences

- E2E coverage asserts this per surface — see `TaskGuide.E2E` in `tests/TEST-INVENTORY.md`.
- Full record: `docs/research/safari-date-picker-dismissal.md` §8.
- **Standing caveat:** one device, one OS version, one moment. A negative result is weaker evidence
  about the future than a positive one would have been.

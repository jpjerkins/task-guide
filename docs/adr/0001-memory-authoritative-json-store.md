# ADR-0001 — The store is memory-authoritative, mirrored to whole JSON files

**Status:** Accepted · **Source:** [#23](https://github.com/jpjerkins/task-guide/issues/23), amended by [#47](https://github.com/jpjerkins/task-guide/issues/47) and [#52](https://github.com/jpjerkins/task-guide/issues/52) · **Proven in production** 2026-08-26

## Context

`task-guide` is single-user, single-writer, and stores a realistic **single-digit MB after a
decade**. Three decisions that were already settled quietly assumed this shape:

- **Opportunities** is *"computed live on read, no caching"* ([#18](https://github.com/jpjerkins/task-guide/issues/18)) — but it walks a 7-day horizon of Windows × every eligible Task. That is trivial only against memory.
- The firing engine recomputes the whole day's shape every ~30 s ([#16](https://github.com/jpjerkins/task-guide/issues/16)) — disk-authoritative would re-read the same files 2,880 times a day.
- The startup **assert → snapshot → migrate → sweep** phase ([#21](https://github.com/jpjerkins/task-guide/issues/21)) has nowhere to live in a disk-authoritative design.

## Decision

**The whole store loads at startup into typed objects. Every read is served from memory. Every
mutation updates memory and writes the affected file(s) before the request returns.**

- **One global write lock.** A read never blocks on a write and never sees a torn view.
- **Atomic whole-file replace** — write to a temp file, `fsync`, rename. Never an in-place edit.
- **One file per collection**, not per instance and not per date.
- **Type-prefixed ULIDs** — `w_01ARZ3NDEKTSV4RRFFQ69G5FAV`. Lexicographic order is chronological
  order; the prefix makes a stray id self-describing in the audit trail.
- **Unknown fields are preserved at the top level of each record** — captured on read and
  re-emitted on write, by hand in every codec (`CodecPrimitives.UnknownFields`), not by
  `[JsonExtensionData]`. Rollback losslessness depends on it. See *What "preserved" covers* for the
  three places it deliberately stops.

### Layout

```
/data
├── manifest.json           # { "version": 1 }
├── tasks.json
├── day-templates.json
├── patterns.json           # the one envelope: { activePatternId, patterns[] }
├── overrides.json
├── events.json
├── event-exceptions.json
├── completions/
│   ├── <taskId>.json       # every Task has one (#47)
│   └── derived.json
├── fires/<date>.json       # 30-day retention, whole-file rm
├── snapshots/<utc>/        # pre-migration, last 5
└── logs/                   # Serilog rolling daily — Receipts land here
```

### Three time encodings, no overlap

> **Authored values are clock times; recorded facts are instants.**

| Kind | Written as | Where |
|---|---|---|
| Authored clock times | `"17:30"` | Window and Event Start/End |
| Calendar dates | `"2026-08-15"` | Override/Event date, Deadline, Defer, Postpone, completion `due` |
| Recorded instants | `"2026-08-15T22:45:03Z"` | `CreatedAt`, `firedAt`, `dueAt`, completion `done` |

### What "preserved" covers

_Amended 2026-08-29 ([#52](https://github.com/jpjerkins/task-guide/issues/52) — "Storage layer —
outstanding defects and deferred findings"). The original wording promised more than the store
delivers._

The channel exists for exactly one situation: a newer binary added a field, wrote the file, and
someone rolled back to an older one. It carries **no domain data** — a codec's list of known fields
is the whole schema, and anything else in the JSON belongs to a future version. Today every extras
dictionary in a live store is empty.

Three limits, all deliberate:

- **Top level only.** A field a newer binary adds *inside* a nested object — an availability window,
  an event prototype, a dimension entry, an event-exception row — is dropped on the next write.
  Uniform across all nine codecs.
- **`manifest.json` has no channel.** It is the one file a restore may not omit, and the only one
  that preserves nothing.
- **The completion log must not have one.** A `CompletionEntry` has no id of its own, so extras can
  only be keyed by list position — which reattaches them to the *wrong* entry the moment an entry is
  inserted or removed, and drops the trailing ones when the log shortens. `(due, done)` would key it
  correctly (`done` is non-null on every entry), but a rollback that both matters *and* finds a
  preserved field in a completion log is a scenario a single-user store will not meet. The channel
  is removed rather than re-keyed.

**Consequence:** a rollback across a version that added a nested field, a manifest field, or a
completion field loses that data. Additive fields at the top level of every other record survive,
which is what "most schema changes need no migration" rests on.

## What this forbids

- **Do not add a second writer**, in-process or out. One writer is what makes memory-authoritative
  safe without inventing coordination. This is why the scheduler is in-process (ADR-0002) and the
  deployment is one container with `stop-first` (ADR-0003).
- **Do not create `settings.json`**, or any file whose name invites the next knob. New behaviour
  arrives as a new rule, not as configuration.
- **Do not store the Dimension registry.** It is code, asserted at startup.
- **Do not give the completion log an extras channel**, and do not key any extras channel by list
  position. Identity comes from a field, or the channel does not exist.
- **Do not store a derived value.** `status` was removed from `tasks.json`, not migrated, because
  nothing read it back. Opportunities, Orphan-ness and `Unused` are likewise never persisted.
- **Do not partition Overrides or Events by date.** Fires are partitioned because the day is the
  unit you *delete*; Overrides and Events are kept indefinitely and read by *range*.

## Consequences

- **A restore requires the service stopped.** Files restored under a running service are invisible
  to memory, then destroyed by the next mutation. This is a documented failure mode
  ([#49](https://github.com/jpjerkins/task-guide/issues/49)), not a bug to fix.
- Most schema changes need **no migration**: additive fields take defaults on read.
- Snapshots are whole-file copies so they are restorable by someone with `cp` and no tooling.

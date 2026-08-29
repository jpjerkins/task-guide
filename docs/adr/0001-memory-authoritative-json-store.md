# ADR-0001 — The store is memory-authoritative, mirrored to whole JSON files

**Status:** Accepted · **Source:** [#23](https://github.com/jpjerkins/task-guide/issues/23), amended by [#47](https://github.com/jpjerkins/task-guide/issues/47),
[#52](https://github.com/jpjerkins/task-guide/issues/52), [#58](https://github.com/jpjerkins/task-guide/issues/58) and
[#59](https://github.com/jpjerkins/task-guide/issues/59) · **Proven in production** 2026-08-26

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
  **The guarantee stops at one file**: a mutation that writes several is not atomic as a whole. See
  *Atomic is per file, and that is the whole guarantee*.
- **One file per collection**, not per instance and not per date.
- **Type-prefixed ULIDs** — `w_01ARZ3NDEKTSV4RRFFQ69G5FAV`. Lexicographic order is chronological
  order; the prefix makes a stray id self-describing in the audit trail.
- **Unknown fields are dropped, not preserved.** A codec's list of known fields is the whole
  schema; anything the JSON carries beyond it is written back missing. See *Rollback is lossy, and
  that is accepted*.

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

### Atomic is per file, and that is the whole guarantee

_Amended 2026-08-29 ([#59](https://github.com/jpjerkins/task-guide/issues/59) — "Amend ADR-0001 for
the scope of atomic whole-file replace"), via map [#54](https://github.com/jpjerkins/task-guide/issues/54)._

"Atomic whole-file replace" is a claim about **one** file. `MutateAsync` takes a list of writes and
applies them in order, each a temp-file–`fsync`–rename on its own; nothing spans them. A crash, or an
IO failure, between two writes in one list leaves a mutation half on disk.

**Multi-file atomicity is ruled out, not deferred.** Nothing short of genuinely atomic multi-file
writes prevents the half-written mutation, and that realistically means one file for the whole store
— trading away the layout this ADR chose (one file per collection, restorable by someone with `cp`)
to defend a case a single-user, single-writer store meets rarely. Rejected on cost.

Three things stand in its place:

- **Write the record whose survival makes the inconsistency detectable, first.** For
  Event-plus-Override that is the Event: a crash after it lands leaves exactly the condition the
  overlap check looks for, so the next read re-offers the prompt and the store heals itself. This is
  why `StoreMutation.OrderedWrites` is an ordered list and not a set — the order is the mitigation.
- **No conscious abort may follow a write** ([ADR-0009](0009-startup-upgrade-and-the-decide-write-phase-split.md)).
  The half that is under our control is that *we* never stop a sequence half-written. The disk is not
  under our control, and this ADR does not pretend otherwise.
- **`fsync` before the rename**, so the file that lands is whole even when the process does not
  survive the write, and the rename is atomic on the same filesystem. **The containing directory is
  not fsynced** — .NET exposes no portable API for it — so a host crash between the rename and the
  directory entry reaching stable storage could in theory lose the rename. Accepted for the walking
  skeleton; the caveat is written where the discipline lives, and
  [#64](https://github.com/jpjerkins/task-guide/issues/64) makes that one place rather than two.

**Consequence: after a partial write, memory and disk disagree until restart.** A write that throws
part-way leaves the earlier files on disk, sets `LastWriteSucceeded` false — which Liveness
*observes* rather than probes ([#25](https://github.com/jpjerkins/task-guide/issues/25)) — and
**does not swap the read view**. The process goes on serving the pre-mutation state while disk holds
part of the new one. That is the deliberate choice: the alternative is publishing a view assembled
from writes that did not all land. The divergence resolves on restart, which reloads from disk, and
the health signal is how you learn to restart.

### Rollback is lossy, and that is accepted

_Amended 2026-08-29 ([#58](https://github.com/jpjerkins/task-guide/issues/58) — "Decide which
records get an extras channel, everywhere it is missing"). The store carried a hand-threaded
unknown-field channel on seven record types. It is removed, everywhere, rather than completed._

The channel existed for exactly one situation: a newer binary added a field, wrote the file, and
someone rolled back to an older one. Two facts retire it.

**A rollback across a migration never needed it.** That path is snapshot restoration
([ADR-0009](0009-startup-upgrade-and-the-decide-write-phase-split.md)) — the snapshot is taken
before the walk and carries `manifest.json` with it. Preservation was defending a case already
defended.

**A rollback within a store version is a rollback minutes after a bad upgrade.** A purely additive
field does not bump the store version (that is what "most schema changes need no migration" means),
so an older binary opens the store and silently drops the field. The *breadth* of that loss is
total, not partial: writes are whole-file, so one Task edit after the rollback rewrites `tasks.json`
and drops the field from every row at once. What makes it acceptable is the *content*. A rollback
happens quickly, because an upgrade misbehaved; an hour into a bad release nothing has been authored
into a field whose UI you have just rolled away from. The channel preserved defaults.

Against near-zero value stood a real cost: ~150 hand-threaded references across nine codecs, a
signature burden severe enough that `PatternCodec.Read` returns a three-tuple. And it had already
drifted — ten locations lacked a channel, and `CompletionCodec` kept an index-keyed one this very
ADR forbade. A mechanism that must be remembered in ten places is a mechanism that will be forgotten
in ten places.

> **Loose Tags are not this mechanism.** A Tag you invent before any Dimension claims it is
> **domain data** in the declared `looseTags` field, read into `TagSet.LooseTags` — kept, shown, and
> promoted the day you declare the Dimension (`CONTEXT.md`, *Inert Tag*). Inventing a Tag now and
> coding its rule later is a first-class feature of the model and is untouched by this. The channel
> only ever held bytes no version of the domain has a name for.

**Consequence:** rolling a binary back across an additive schema change loses the added field. Roll
forward again and it is gone, not restored. Crossing a store version instead restores a snapshot,
which loses nothing.

## What this forbids

- **Do not add a second writer**, in-process or out. One writer is what makes memory-authoritative
  safe without inventing coordination. This is why the scheduler is in-process (ADR-0002) and the
  deployment is one container with `stop-first` (ADR-0003).
- **Do not create `settings.json`**, or any file whose name invites the next knob. New behaviour
  arrives as a new rule, not as configuration.
- **Do not store the Dimension registry.** It is code, asserted at startup.
- **Do not re-add an unknown-field preservation channel**, to any record, at any nesting level, by
  hand or via `[JsonExtensionData]`. It was removed for being all cost and no value, and it is worth
  less on each re-add than it looks. Enforced by test, not by prose — prose forbade the index-keyed
  completion channel and the code kept it anyway.
- **Do not append a write to a mutation list without deciding where in the list it goes.** The order
  is the mitigation for the half-written mutation; a write added at the end because that was the
  easy place to add it silently picks a failure mode.
- **Do not store a derived value.** `status` was removed from `tasks.json`, not migrated, because
  nothing read it back. Opportunities, Orphan-ness and `Unused` are likewise never persisted.
- **Do not partition Overrides or Events by date.** Fires are partitioned because the day is the
  unit you *delete*; Overrides and Events are kept indefinitely and read by *range*.

**What a read guarantees its caller** — duplicate keys rejected at read, the two arms of absence, and
`BadStoreFileException` as the single catchable failure type — is
[ADR-0010](0010-store-read-contract.md), not this ADR.

**The startup upgrade model** — forward only, no down-migrations, `manifest.json` written once at the
end, a version ahead refusing to start, and a fresh `/data` bootstrapping *without* taking a snapshot
(there is nothing yet for one to protect) — is
[ADR-0009](0009-startup-upgrade-and-the-decide-write-phase-split.md), not this ADR.

## Consequences

- **A mutation is atomic per file, never across files.** Order the writes so that a crash between
  them leaves a detectable inconsistency, and let the next read heal it.
- **A restore requires the service stopped.** Files restored under a running service are invisible
  to memory, then destroyed by the next mutation. This is a documented failure mode
  ([#49](https://github.com/jpjerkins/task-guide/issues/49)), not a bug to fix.
- Most schema changes need **no migration**: additive fields take defaults on read — and an
  older binary meeting a newer one's file drops what it cannot name.
- Snapshots are whole-file copies so they are restorable by someone with `cp` and no tooling.

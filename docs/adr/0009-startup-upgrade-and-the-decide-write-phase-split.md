# ADR-0009 — Startup upgrades run forward only, and the decide phase asserts before the write phase begins

**Status:** Accepted · **Source:** [#57](https://github.com/jpjerkins/task-guide/issues/57), via [#54](https://github.com/jpjerkins/task-guide/issues/54)  
**Amended** 2026-09-03 — the two-phase bootstrap, from [#67](https://github.com/jpjerkins/task-guide/issues/67) via [#75](https://github.com/jpjerkins/task-guide/issues/75).

## Context

> **`StartupSequence` no longer exists.** [#78](https://github.com/jpjerkins/task-guide/issues/78)
> retired it on 2026-09-05, implementing the amendment below: its work is now split across
> `StartupPlanner` (plans, may refuse, cannot write), `StartupWriter` (applies, refuses nothing) and
> `StartupBootstrap` (wires the two, then opens the runtime store). The Context below is kept as
> written — it is the history this decision was made against, not a description of today's code.

`StartupSequence.RunAsync` is the composition root's entry point, and it is the only place in the
system that makes several decisions and several writes in one sequence: assert the registry, plan
the migration, seed an empty store, snapshot, migrate, sweep. Every other write path is a single
`IStore.MutateAsync` call, where the whole write list is built by the callback before the first byte
moves — nothing can interleave.

Two things about that sequence were real, load-bearing, and written down only in comments.

**The migration model.** Nothing decided that upgrades are forward-only, that `manifest.json` is
written once at the end rather than per step, or that a version ahead of this binary refuses. ADR-0001
lists "startup" among the things to read it before touching, but decides none of this.

**The ordering.** A refusal that fires *after* a write leaves the store partly modified by a startup
that then declined to run. This is not hypothetical: `SeedDefaultPatternAsync` once ran above the
`PlanMigration()` that raises `StoreVersionAheadException`, so refusing to start landed two files on
disk first. It was fixed by moving `PlanMigration()` up (`dd76324`), and a comment was left explaining
why the order matters — but a comment is not enforcement, and the next edit to reorder those lines
reintroduces it.

## Decision

### The migration model

- **Forward only.** The walk goes N→N+1 from the version in `manifest.json`, following the ordered
  steps supplied at construction. There are no down-migrations, and none will be added: a rollback
  restores a snapshot, it does not un-migrate.
- **A version ahead of this binary refuses to start.** An older binary must never open a store a newer
  one has migrated — silently down-migrating it is the one outcome worse than refusing.
- **Moving strictly forward is an invariant of the step, not a discovery of the walk.**
  `StoreMigration` rejects `To <= From` at construction, so a non-monotonic step — which would let the
  walk cycle and hang startup forever — cannot be built. It is a sealed class rather than a record for
  this reason: a record's copy constructor is a second door (`step with { To = … }`) that bypasses the
  invariant.
- **`manifest.json` is written exactly once, after every step has succeeded.** A step that throws leaves
  the file at its pre-migration version, so a retried startup attempts the whole walk again rather than
  resuming a partial one.
- **A missing `manifest.json` is a fresh `/data`,** never migrated by any binary. It bootstraps the file
  at `CurrentVersion` and takes no snapshot — there is nothing yet for a snapshot to protect.
- **A snapshot is taken only when a migration or a registry sweep is actually about to write,** and it
  copies `manifest.json` along with the collection files. A restored set without its version stamp hands
  already-migrated data to a binary that migrates it again.
- **The registry sweep runs after the migration, never before.** The order is snapshot → migrate →
  sweep, and only the first two are conditional. The sweep promotes loose Tags the registry now
  claims, so a step that adds or renames a Dimension value changes what it is sweeping *for*;
  sweeping first promotes against a registry the data has not reached yet. This is intent rather than
  full effect today — `JsonStore`'s read view is loaded at construction, so the sweep still sees
  pre-migration data, and closing that needs a store-reload API
  ([#53](https://github.com/jpjerkins/task-guide/issues/53)).

### The decide/write phase split

**The decide phase asserts; the write phase does not refuse.** Every conscious refusal is raised
before the first write. Once writing has begun, no decision of ours may stop it — the sequence runs
to completion or fails on IO.

This constrains where a refusal may be *raised*, not where code may be *called*. Shared planning code
may run again during the write phase; recomputing is not the concern, and reuse is often the right
thing. What must hold is that its refusal path cannot be reached from there. When that is true only
because of what the write phase happens not to touch, say so at the call site — a reviewer needs to
know it is a reachability argument, not an accident.

None of this speaks to IO failure: storage fails when it wants to, and a partially-written mutation is
the accepted cost (ADR-0001). The decide phase asserts that *we* will not stop the write, never that
the disk will cooperate.

**The worked example.** `RunAsync` plans the migration, then seeds, then calls `MigrateAsync`, which
calls `PlanMigration()` **again** — and that call can raise `StoreVersionAheadException`. It looks like
a violation and is not one: the re-plan's verdict turns on the store version, and the write between the
two calls cannot change it. That is the reachability argument this rule asks to be stated out loud.

### Amendment — the pre-serving bootstrap is two phases, then the store opens

*Added 2026-09-03 from [#67](https://github.com/jpjerkins/task-guide/issues/67).* The phase split
above is a rule about **where a refusal may be raised** inside one sequence. The composition root
makes it **structural**: the phase that may refuse cannot write, because it is never handed anything
that writes.

1. **Plan.** A storage-owned `IStartupPlanner` sees the store only through a **read-only view over a
   disposable bootstrap snapshot**. It raises *every* conscious refusal — a version ahead of this
   binary, a duplicate key, anything else that stops startup — and returns an immutable
   `StartupPlan`. It writes nothing.
2. **Apply.** A storage-owned writer applies an **already-valid** plan. It makes no conscious refusal
   at all; only IO may stop it.
3. **Open.** Only then does a factory open the memory-authoritative runtime store, whose read view is
   loaded fresh from what the write phase just landed.

**The read contract is shared; the two implementations are not.** The runtime store's interface
**extends** a read-only store interface that the bootstrap view also satisfies, so a planner cannot be
handed something that writes and the two views cannot drift apart in what they promise a reader.

> **That read-only interface does not exist in `src/` on the day this ADR lands.**
> [#76](https://github.com/jpjerkins/task-guide/issues/76) introduces it as `IStoreReader`, with
> `IStore` extending it. Until that merges, the three paragraphs above state the contract the
> bootstrap must uphold, not a type you can go and read.

**The bootstrap snapshot is never registered in runtime DI**, and is disposed once the plan is
applied. A snapshot still reachable from a request handler is a second read view of the store that
stops agreeing with it the moment anything writes.

**`Program.cs` awaits the bootstrap before `builder.Build()`.** The infrastructure-owned
bootstrap-and-open-store operation is awaited explicitly and the *completed* store is registered.
There is no temporary service provider and no uninitialised store holder filled in later — either one
lets a registration observe a store that has not finished deciding. The normal host then registers
immutable domain policy and the canonical Dimension registry, one injectable clock, the completed
store, and a stateless `DayShapeReader` as `IDayShapeReader`; the reader caches nothing and never
writes.

**This changes nothing about the migration model.** Upgrades stay forward-only, and a rollback is
still restore-the-snapshot-then-deploy-the-older-binary rather than a reverse migration. The
amendment moves *where the refusal lives*, not *what refuses*.

Three things must be provable:

- every planner refusal leaves **no directory entry changed** — listed whole, per the enforcement
  note in *Consequences* below;
- a valid plan writes **in order** and opens a fresh runtime view, including a readable day shape
  over an empty store (ADR-0008);
- **host creation** refuses a future-version store, before any endpoint or the tick loop can start.


## What this forbids

- **Do not add a conscious refusal below the first write in a startup sequence.** If a new condition
  must stop startup, it is decided in the decide phase or it is not decided at all.
- **Do not check step monotonicity in the walk.** It is checked where the step is built; a second check
  downstream would suggest the invariant is not trusted, and would rot the day the walk is rewritten.
- **Do not make `StoreMigration` a record**, however tidy it looks. `with` is a door around its invariant.
- **Do not write `manifest.json` per step.** Partial walks are not resumable; the whole walk is the unit.
- **Do not add a down-migration.** The rollback path is snapshot restoration, and it is deliberate that
  there is only one.
- **Do not move the registry sweep above `MigrateAsync`.** Nothing in the suite fails if you do — the
  sweep's own no-op guard hides it — which is exactly why the order is written here.
- **Do not hand the planner anything that can write.** The phase split is structural here; a planner
  taking the runtime store "just to read" reduces it back to a convention about line order.
- **Do not register the bootstrap snapshot in runtime DI**, and do not keep it alive past the apply
  phase.
- **Do not build a temporary `ServiceProvider` to run startup**, and do not register a store holder
  that is empty at registration time. Both make "the store is open" a runtime hope rather than a
  fact `Program.cs` has already awaited.

## Consequences

- Rolling a deployment back across a migration is a **manual** operation: restore the pre-migration
  snapshot, then deploy the older binary. That path is intact — the snapshot is taken before the walk and
  includes `manifest.json` — but it has never been exercised. See
  [#49](https://github.com/jpjerkins/task-guide/issues/49).
- `completions/*` and `fires/*` are excluded from the snapshot set: neither carries a Dimension value and
  no step touches them. **The first migration step that touches either must widen the snapshot set**, or a
  rollback silently loses them.
- The phase split is enforced by test rather than by structure: each conscious refusal has a test asserting
  the data directory is unchanged, listing the whole directory rather than checking named files absent, so
  it catches writes nobody anticipated. Structure — a type that cannot write before it has decided — was
  considered and rejected as too large a change to the storage layer to protect one sequence.
- Today all of this is latent: `CurrentVersion` is 1 and `StoreMigrations.Ordered` is empty, so no store has
  ever been migrated. The rules exist to be true before the first migration, not after it.
- **The sweep-sees-pre-migration-data gap above is still open.** The two-phase bootstrap opens the
  runtime store after the write phase, so the *runtime* never reads a stale view — but the sweep runs
  inside the write phase, against whatever view that phase holds. #67 did not settle this, and the
  store-reload question ([#53](https://github.com/jpjerkins/task-guide/issues/53)) is still what
  closes it. Recorded so the amendment is not mistaken for a fix.

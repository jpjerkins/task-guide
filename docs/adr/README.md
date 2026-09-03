# Architecture Decision Records

Decisions a coding agent would otherwise violate. **Read the ones that touch your area before you
write code.** They are deliberately few — the full reasoning lives in the linked issues, and the
ubiquitous language lives in `CONTEXT.md` (start at `CONTEXT-INDEX.md`).

| ADR | Decision | Read it before touching |
|---|---|---|
| [0001](0001-memory-authoritative-json-store.md) | Memory-authoritative store, atomic whole-file JSON writes, ULIDs | storage, persistence, ids, startup |
| [0002](0002-stack.md) | .NET 10, Minimal APIs, React SPA, one container | any new project, endpoint or SPA type |
| [0003](0003-deployment.md) | Swarm via DCM, host-mode 8007, tailnet-only, `stop-first` | deployment, secrets, the Dockerfile |
| [0004](0004-ranking.md) | Four derived ranking keys; no priority field; ranking never removes from the matched set | matching, ranking, Scarcity, Opportunities |
| [0005](0005-firing-engine.md) | One ~30 s tick of predicates; no timers, no catch-up; a planner decides and an executor delivers | the tick loop, Fire records, notifications |
| [0006](0006-controls-survive-their-own-input-events.md) | Never remount a control in its own input handler | any date, time, select or slider control |
| [0007](0007-status-is-derived-and-gates-null-duration.md) | Status derived; the gate is what handles null Duration | Task shape, matching, anything reading Duration |
| [0008](0008-empty-store-guarantee.md) | An empty store must start; the default Pattern's content is not a contract | the startup seed, first-run behaviour, tests over a fresh store |
| [0009](0009-startup-upgrade-and-the-decide-write-phase-split.md) | Forward-only startup upgrades; every conscious refusal is raised before the first write; the bootstrap plans, then writes, then opens the store | `StartupSequence`, the composition root, migration steps, snapshots, anything that refuses at startup |
| [0010](0010-store-read-contract.md) | Duplicate keys rejected at read; two arms of absence; one catchable failure type | any codec, store loading, day-shape or Pattern reads |
| [0011](0011-nullable-strictness-and-union-representation.md) | Nullable strict repo-wide; closed sets are `OneOf` unions; compare them with `.Equals` | any closed set, any switch over one, any new Domain dependency |

**Amendments are dated in the ADR's own header.** 0004, 0005 and 0009 each carry a 2026-09-03
amendment from the application-layer resolutions (#67, #68); read the amendment sections, not only the
original Decision.

## Three lines that recur across all ten

- **Facts stored, everything else derived.** Status, Opportunities, Orphan-ness and `Unused` are
  computed on read and never persisted.
- **New behaviour arrives as a new rule, not as configuration.** There is no settings file, no knob,
  no weight to tune. If you are about to add one, you are contradicting an ADR.
- **Blast radius is made visible, not prevented.** Warn with a count before committing; do not block.

## If your work contradicts one

Surface it explicitly rather than silently overriding:

> _Contradicts ADR-0005 (firing engine) — but worth reopening because…_

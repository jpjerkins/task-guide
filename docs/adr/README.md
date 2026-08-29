# Architecture Decision Records

Decisions a coding agent would otherwise violate. **Read the ones that touch your area before you
write code.** They are deliberately few — the full reasoning lives in the linked issues, and the
ubiquitous language lives in `CONTEXT.md` (start at `CONTEXT-INDEX.md`).

| ADR | Decision | Read it before touching |
|---|---|---|
| [0001](0001-memory-authoritative-json-store.md) | Memory-authoritative store, atomic whole-file JSON writes, ULIDs | storage, persistence, ids, startup |
| [0002](0002-stack.md) | .NET 10, Minimal APIs, React SPA, one container | any new project, endpoint or SPA type |
| [0003](0003-deployment.md) | Swarm via DCM, host-mode 8007, tailnet-only, `stop-first` | deployment, secrets, the Dockerfile |
| [0004](0004-ranking.md) | Four derived ranking keys; no priority field | matching, ranking, Scarcity, Opportunities |
| [0005](0005-firing-engine.md) | One ~30 s tick of predicates; no timers, no catch-up | the tick loop, Fire records, notifications |
| [0006](0006-controls-survive-their-own-input-events.md) | Never remount a control in its own input handler | any date, time, select or slider control |
| [0007](0007-status-is-derived-and-gates-null-duration.md) | Status derived; the gate is what handles null Duration | Task shape, matching, anything reading Duration |
| [0008](0008-empty-store-guarantee.md) | An empty store must start; the default Pattern's content is not a contract | the startup seed, first-run behaviour, tests over a fresh store |
| [0009](0009-startup-upgrade-and-the-decide-write-phase-split.md) | Forward-only startup upgrades; every conscious refusal is raised before the first write | `StartupSequence`, migration steps, snapshots, anything that refuses at startup |

## Three lines that recur across all nine

- **Facts stored, everything else derived.** Status, Opportunities, Orphan-ness and `Unused` are
  computed on read and never persisted.
- **New behaviour arrives as a new rule, not as configuration.** There is no settings file, no knob,
  no weight to tune. If you are about to add one, you are contradicting an ADR.
- **Blast radius is made visible, not prevented.** Warn with a count before committing; do not block.

## If your work contradicts one

Surface it explicitly rather than silently overriding:

> _Contradicts ADR-0005 (firing engine) — but worth reopening because…_

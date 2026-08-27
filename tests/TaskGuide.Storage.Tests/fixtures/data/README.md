# The golden store

A complete, valid `/data` directory as one deployed binary would write it. This is the on-disk
contract from [Storage file layout](https://github.com/jpjerkins/task-guide/issues/23), emitted as
files rather than as prose so the storage tests load it and the shapes are *checked* rather than
described.

Three encodings, no overlap:

| Kind | Written as | Where |
|---|---|---|
| Authored clock times | `"17:30"` | Window and Event start/end, Event prototype times |
| Calendar dates | `"2026-08-15"` | Override and Event date, Deadline, Defer, Postpone, completion `due` |
| Recorded instants | `"2026-08-15T22:45:03Z"` | `createdAt`, `firedAt`, `dueAt`, completion `done` |

Two departures from #23's file shapes, both settled after it closed:

- **`tasks.json` carries no `status`** — Status is derived (#47). The field was *removed*, not
  migrated, because nothing read it back.
- **`overrides.json` carries `used`** — the Day template use record from #24, `{ templateId,
  templateName }` with the name captured at write time. #23 ruled out a `stampedFrom` *reference*;
  this is a use record, never followed to resolve a shape, only counted and displayed.

Not represented here, deliberately: `snapshots/` (written only by a startup that will migrate) and
`logs/` (Serilog's, with its own retention — Receipts land there and nothing queries them).

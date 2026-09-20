# ADR-0010 — What a codec read guarantees: uniqueness, absence, and one catchable failure type

**Status:** Accepted · **Source:** [#52](https://github.com/jpjerkins/task-guide/issues/52), via [#54](https://github.com/jpjerkins/task-guide/issues/54) and [#66](https://github.com/jpjerkins/task-guide/issues/66)  
**Amended** 2026-09-20 — a third arm of absence, for an optional property on a stored record, from [#153](https://github.com/jpjerkins/task-guide/issues/153) via [#162](https://github.com/jpjerkins/task-guide/issues/162).

## Context

Loading the store is the one place where a file on disk becomes an in-memory fact. Every codec does
it, and each one has quietly answered three questions for itself:

- **Does a duplicate row get through?** `FireCodec` rejects duplicates on `(date, windowId, kind)`
  and names the offender. `EventCodec.ReadExceptions` did not — and two rows sharing
  `(date, prototypeId)` loaded cleanly, then made `DayShapeReader`'s `SingleOrDefault` throw on
  **every** read of that date. The day was permanently unreadable, with no self-heal.
- **What does absence mean?** `CompletionsFor` and `FiresOn` return an empty record. `PatternBook.Active`
  and `DayShapeReader` throw. Read as one convention these look contradictory, and the temptation is
  to unify them.
- **What does a caller catch?** Reads throw `JsonException`, `InvalidOperationException`,
  `KeyNotFoundException` and `NullReferenceException` depending on where the file is malformed, so
  "the store file is bad" has no single catchable shape.

These are one decision, not three: the exception type is *how* the other two report themselves. They
are written down here rather than as three amendments to ADR-0001 — already amended twice — because
they are only comprehensible together.

## Decision

**a. Where a record type has a natural key, the read rejects duplicates and names the offending key.**

The check belongs at read, in the codec, next to the row parsing — not at write. `FireCodec.RejectDuplicateKeys`
is the worked example; the keyed reads are Fire rows on `(date, windowId, kind)`, event exceptions on
`(date, prototypeId)`, and derived completions on `(ruleId, triggerId, due)`. Where a codec keys its
extras channel, that key **is** the uniqueness key — the two cannot drift apart.

The message names the key's values, not just the fact of a collision, because the operator's next act
is to open the file and delete a row.

**b. Absence has two arms, and they are not in conflict.**

> **There is a third arm since 2026-09-20 — see decision d below.** Everything in b is about
> **sparse collection files** and dangling references, and its "never null" does not reach an
> optional property on a record that did load. Reading b as a rule about record properties is the
> mis-citation d was written to stop ([#162](https://github.com/jpjerkins/task-guide/issues/162)).

- **Sparse-collection absence reads as empty.** `CompletionsFor` / `FiresOn` — per-task and per-date
  files where *having no file* is the normal state of a healthy store. Never null, never a throw.
- **A dangling reference throws, naming what dangled.** `PatternBook.Active` (an active-Pattern id
  matching no Pattern) and `DayShapeReader` (a Pattern naming an absent Day template). Absence here
  means the loaded store is internally inconsistent, and continuing would invent data.

The distinction is *what absence means*, not *what shape the caller wants*. Unifying is wrong in both
directions: an empty `Active` Pattern has nothing to return, and throwing on an untouched day would
make a fresh store unreadable — contradicting ADR-0008.

A dangling-reference message names **both** ends: the id that was not found and the record that
referenced it. "Sequence contains no matching element" identifies neither, and the operator cannot
act on it.

**c. Every read failure surfaces as one catchable type — `BadStoreFileException`.**

Wrapped at each codec boundary, not at the ~70 individual `GetProperty` sites.
[#63](https://github.com/jpjerkins/task-guide/issues/63) specifies and implements this arm; the rule is
stated here so it binds every codec written after it, not only the ones #63 touches.

### Amendment — **d.** a third arm: an optional property on a stored record, where *absent* and *empty* are different facts

Decision b was written against **sparse collection files**, and its examples are all of that shape:
`CompletionsFor` and `FiresOn` read a per-task or per-date *file* whose non-existence is the normal
state of a healthy store. Its "never null, never a throw" is a rule about **how a sparse collection
read reports an absent file** — it does not reach a property *inside* a record that did load.
[#153](https://github.com/jpjerkins/task-guide/issues/153) hit the case it does not cover, cited
decision b for it, and the citation contradicted the code; this amendment is the rule that was
actually needed.

**The arm.** A property may be added to an existing record type such that *"written before this
property existed"* and *"deliberately empty"* are different facts about the record. Then the property
is nullable, and three states are meaningful:

| State | On disk | Meaning |
|---|---|---|
| `null` | property **omitted** | the older behaviour — the value comes from wherever it came from before this property existed |
| present, empty | `"events": []` | the record deliberately holds none; nothing from the old source leaks through |
| present, non-empty | `"events": [ … ]` | the record's own value |

`DateOverride.Events` is the worked example: absent takes the active Pattern's Event prototypes for
the weekday, `[]` is a day with no Events at all, non-empty is the date's own.

**The rules this arm carries.**

- **Read with `TryGetProperty`, never `GetProperty`,** wherever absence is meaningful. `GetProperty`
  throws on the absent arm, which is the arm most of the store is in.
- **Write by omitting the property — never an explicit JSON `null`.** Two encodings for one meaning is
  a drift hazard, and omitting is what keeps a pre-existing golden fixture **byte-identical**, which is
  the evidence that adding the property touched no existing record. #153's `overrides.json` round-trip
  test pins exactly that, and is the test a new use of this arm copies.
- **Equality must not unify the arms.** `null` equals `null`, and `null` never equals empty.
  `StructuralEquality.MultisetEqual` already does this (reference-equal nulls, null vs non-null
  unequal), so it is a property to preserve rather than new work.
- **This is not decision b's dangling reference.** Nothing dangled; nothing is inconsistent. The absent
  arm is a record written by an older binary, which is a normal store, not a broken one.

**When this arm is the wrong tool.** It is a **migration-avoidance device**: it exists so a shape
change does not have to rewrite existing rows. For #153 a migration would have meant choosing, for
every Override ever written, either "no Events" or "the Pattern's Events frozen in" — recording as
fact a decision the user never made. Where a property's absence has **no** natural meaning, do not
reach for this: make it required and migrate. Three states that a reader cannot tell apart from the
data is worse than two states plus a migration.

**The rollback cost, stated.** An older binary does not know the property, so it drops it on the next
whole-file write ([ADR-0001](0001-memory-authoritative-json-store.md), *Rollback is lossy, and that is
accepted*) and the record reverts to the absent arm — the pre-change behaviour, for that record. #153
accepted that rather than bumping `manifest.json`, which would make the older binary **refuse to
start** ([ADR-0009](0009-startup-upgrade-and-the-decide-write-phase-split.md)). That trade is the
reason this arm is additive-only: it must stay a change an old binary can ignore.

## What this forbids

- **Do not ship a codec whose record type has a natural key and no duplicate guard at read.** A
  duplicate that loads is a store that reads today and throws forever tomorrow, at a call site far
  from the file.
- **Do not move a uniqueness check to the write side.** The write path is handed an array; the read
  path is the boundary the whole store crosses on every load, including files edited by hand or
  restored from a snapshot.
- **Do not unify the arms of absence.** Neither "make dangling references empty" nor "make sparse
  absence throw" is a simplification; each breaks a behaviour the other arm exists to provide.
- **Do not throw a bare `Single`/`SingleOrDefault` failure across a dangling reference.** If you write
  `.Single(...)` over a store collection, either it cannot dangle or you owe it a named throw.
- **Do not introduce a new exception type for a read failure.** It goes inside `BadStoreFileException`.
- **Do not write an explicit `null` for a property in arm d, and do not read one with `GetProperty`.**
  Omission is the encoding; `TryGetProperty` is the read. **This binds arm d only** — a plain
  two-state optional, where `null` just means "no value" and no third state exists, keeps the
  explicit-`null` encoding the store already uses (`overrides.json`'s `used`, `tasks.json`'s `notes`,
  `defer` and `recurrence`). Those are correct as written; do not convert them.
- **Do not make an optional property's absence and emptiness compare equal.** They are different
  records, and a round trip that conflates them silently rewrites history.

## Consequences

- A store file that violates a key is refused at load: the app does not start, rather than starting
  and failing later on a specific date. That is deliberate — the failure names the file and the key,
  and one hand-edit fixes it.
- Callers of a sparse read never null-check; callers of a reference read never fall back. The read's
  signature says which is which.
- Each new codec inherits a checklist: key it, decide which arm of absence applies, wrap the boundary.
  **Three arms, since the 2026-09-20 amendment:** a sparse collection file (empty), a dangling
  reference (named throw), and an optional property on a record (`null` ≠ empty, omitted on write).

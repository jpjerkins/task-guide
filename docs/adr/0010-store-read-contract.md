# ADR-0010 — What a codec read guarantees: uniqueness, absence, and one catchable failure type

**Status:** Accepted · **Source:** [#52](https://github.com/jpjerkins/task-guide/issues/52), via [#54](https://github.com/jpjerkins/task-guide/issues/54) and [#66](https://github.com/jpjerkins/task-guide/issues/66)

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

## What this forbids

- **Do not ship a codec whose record type has a natural key and no duplicate guard at read.** A
  duplicate that loads is a store that reads today and throws forever tomorrow, at a call site far
  from the file.
- **Do not move a uniqueness check to the write side.** The write path is handed an array; the read
  path is the boundary the whole store crosses on every load, including files edited by hand or
  restored from a snapshot.
- **Do not unify the two arms of absence.** Neither "make dangling references empty" nor "make sparse
  absence throw" is a simplification; each breaks a behaviour the other arm exists to provide.
- **Do not throw a bare `Single`/`SingleOrDefault` failure across a dangling reference.** If you write
  `.Single(...)` over a store collection, either it cannot dangle or you owe it a named throw.
- **Do not introduce a new exception type for a read failure.** It goes inside `BadStoreFileException`.

## Consequences

- A store file that violates a key is refused at load: the app does not start, rather than starting
  and failing later on a specific date. That is deliberate — the failure names the file and the key,
  and one hand-edit fixes it.
- Callers of a sparse read never null-check; callers of a reference read never fall back. The read's
  signature says which is which.
- Each new codec inherits a checklist: key it, decide which arm of absence applies, wrap the boundary.

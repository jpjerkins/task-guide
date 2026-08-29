# ADR-0008 — An empty store's guarantee is that it starts, not what the default Pattern contains

**Status:** Accepted · **Source:** [#52](https://github.com/jpjerkins/task-guide/issues/52), via [#54](https://github.com/jpjerkins/task-guide/issues/54)

## Context

A brand-new `/data` has no `patterns.json`. `PatternBook.Active` calls `Patterns.Single(...)`, which
throws on an empty list — so without intervention the first day-shape read of a fresh install crashes
the app. `StartupSequence.SeedDefaultPatternAsync` fixes that by writing one vanilla weekly Pattern
("Default", seven days of one "Ordinary day" template) before anything reads.

That seed is now two things at once: a **mechanism** that prevents the crash, and a **sample** of what
a Pattern looks like. Only the first is a promise.

## Decision

**The guarantee is "an empty store does not crash". The default Pattern's content is a choice, and
nothing may depend on it.**

- The guarantee test asserts that startup succeeds and a day shape is readable. It asserts **nothing**
  about the default Pattern's name, its template's name, the number of distinct templates, or the
  emptiness of that template's Windows and Event prototypes.
- Changing the default's content is not a breaking change and needs no migration.

## What this forbids

- **Do not assert the default's content in a test.** A test that pins "Ordinary day" or "Default"
  converts a choice into a contract, and the next person to improve the starter Pattern will
  reasonably delete the test that stops them — losing the crash guarantee with it, because it is the
  same test.
- **Do not treat the seed as onboarding.** If a starter experience is ever wanted, it is a separate
  feature with its own decision; it does not arrive by quietly enriching the default and letting
  tests accrete around it.
- **Do not read the default's shape anywhere in production code.** It is written once and then is
  ordinary user data, indistinguishable from a Pattern the user authored.

## Consequences

- A user's first screen shows a plausible but empty week. That is accepted: an empty week the user
  edits beats a populated one they must first understand and undo.
- The seed writes two files (`day-templates.json`, `patterns.json`) in one mutation and takes no
  snapshot — an empty store has nothing for a snapshot to protect (ADR-0001).

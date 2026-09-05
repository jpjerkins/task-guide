# ADR-0011 — Nullable strictness, and unions as `OneOf` until C# ships its own

**Status:** Accepted · **Source:** [#69](https://github.com/jpjerkins/task-guide/issues/69) · **Decided** 2026-09-01

## Context

Two of the three things this ADR records were already true and merely undocumented: `Directory.Build.props`
has carried `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
repo-wide from the start, and the codebase has honoured them — **zero null-forgiving `!` operators in
`src/`**, and one `#pragma warning disable` in the whole tree (`CA1416`, guarding a POSIX-only call in
a test that returns early on Windows).

The third was not settled. Five Domain types are closed sets modelled as abstract-record hierarchies
(`RecurrenceRule` — whose own doc comment calls it "a closed set" — plus `Defer`, `Offset`,
`Dimension`, `ControlShape`), and #69 added three more union-shaped types to build. C# native
discriminated unions are designed for C# 15 / .NET 11, which is not GA and not installed here; the
only SDK on any machine in this network is 10.0.302.

Recording this now matters because #71 is about to partition the application layer into parallel
implementation lanes. Four agents each inventing their own sum-type idiom is the specific
divergence this ADR exists to prevent.

## Decision

### Nullable strictness is the standard, and it is not negotiable per-project

`Nullable` enabled and `TreatWarningsAsErrors` true stay in `Directory.Build.props`, applying to
every project including tests. A `!` or a `#pragma warning disable` is a **defect with a
justification comment**, not a tool — the existing `CA1416` suppression is the shape that is
acceptable: narrow, on one line, explaining a platform fact the compiler cannot see.

### Union-shaped types use `OneOf`, not sealed hierarchies

```csharp
[GenerateOneOf]
public partial class FetchOutcome : OneOfBase<Known, Unavailable>;

public sealed record Known(IReadOnlyList<TagValue> Values);
public sealed record Unavailable(string Reason);
```

`OneOf` is the representation for every union-shaped type in the Domain, persisted or not. The
three from #69 — `FetchOutcome<T>`, `GlanceState`, and `FireIntent`'s kind (#73) — adopted it from
the start; `RecurrenceRule`, `Defer` and `Offset` are persisted through hand-written codecs whose
JSON shape is independent of the in-memory representation (#72), and **the stored shape must not
move when the representation changes**.

**The gain is exhaustiveness on adding a case, and it is the whole argument.** A sealed hierarchy
cannot be proven exhaustive by the compiler, because the abstract base is derivable, so every switch
needs a `_ => throw` arm — which means adding a sixth case compiles cleanly and fails at *runtime*.
`OneOfBase.Match` takes one lambda per arm, so a new case breaks every call site at *compile* time.
Verified on OneOf 3.0.271 under .NET 10: widening `OneOfBase<A, B>` to `OneOfBase<A, B, C>` produced
`CS7036` at each `Match`.

What it does **not** buy is smaller surface area: the case types are declared either way. What moves
is where behaviour lives — from virtual overrides on each case, to `Match` at the call site. That is
already this codebase's prevailing style: behaviour lives in static rule classes (`DeferRules`,
`RecurrenceRules`, `FiringPolicy`, `SnoozePolicy`, `OrphanDetection`, `ClockTimeResolution`,
`TimeToLivePolicy`) that take a record and return a value, so `OneOf` pushes toward the existing
grain rather than against it.

### `==` on a union base is reference equality — use `.Equals`

**Verified, not assumed.** For `partial class X : OneOfBase<...>`, `.Equals` and `GetHashCode` are
structural, but `==` is **reference** comparison, because a `class` gets no synthesised equality
operators the way a `record` does.

This is load-bearing, not pedantry. #69 decided the Glance floor compares `GlanceState` to suppress
a redundant send; a silent `==` there would report "changed" on every tick and exhaust watchOS's
50-updates-a-day budget in about 25 minutes. Compare unions with `.Equals`, and cover it with a test
that asserts two structurally equal states are equal.

### The Domain's dependency rule, restated as its principle

`TaskGuide.Domain.csproj` said "no package references". The intent was never purity for its own
sake — it was **no I/O, no framework coupling, and no external reason to change**. `OneOf` violates
none of those: it is a type-level library with no runtime behaviour of its own.

The rule is therefore restated: *the Domain takes no dependency that performs I/O, couples it to a
framework, or gives it a reason to change for a cause outside the domain.* A blanket ban is easier to
check but was already the wrong test; this one is checkable against something real.

## What this forbids

- **Do not add a `_ => throw` discard arm to a switch over a `OneOf` union.** If you need one, you
  are switching on something that is not actually closed.
- **Do not compare unions with `==`.** See above.
- **Do not use `[GenerateOneOf]` in the global namespace** — the generator crashes with `CS8785`
  (`hintName` contains `<`). Every type in this repo is namespaced, so this only bites in a scratch
  project.
- **The five existing hierarchies are no longer sealed hierarchies.** #72 retrofitted all five
  (`RecurrenceRule`, `Defer`, `Offset`, `Dimension`, `ControlShape`) to `OneOf` unions. The
  transitional split this ADR described — new types now, existing five later, under a single
  owner — is closed.
- **Do not add a .NET 11 preview SDK to chase native unions.** .NET 10 is LTS and proven on the Pi;
  .NET 11 is STS and not GA. Revisit at GA — and note that migrating from `OneOf` to native unions
  is a rewrite of every `Match` call site, so the retrofit in #72 should weigh whether to wait.

## What was rejected

- **Sealed hierarchies plus a throwing discard arm** — the status quo. Rejected for the runtime-vs-
  compile-time exhaustiveness gap above. It was the right shape for the five existing types until
  #72 retrofitted them, which was a sequencing judgement, not a disagreement about representation.
- **Splitting the idiom by layer** — `OneOf` in Application/Infrastructure, sealed hierarchies in
  Domain, to keep the Domain package-free. Rejected: the line is arbitrary to a reader, and it would
  have handed two idioms to the parallel lanes. The transitional split that *is* accepted — new
  types now, existing five in #72 — is bounded by a ticket and a stated end state.
- **Nullable reference types alone as the absence idiom.** Still correct for plain absence
  (`ResolvedWindow?` for a spring-gap Window, `bool? Writable` for a write not yet attempted). A
  union is for a choice *between* shapes, not for "might not be there".

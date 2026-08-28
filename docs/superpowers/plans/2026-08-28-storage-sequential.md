# Storage — the sequential phase, part A

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** finish `JsonStore` so the whole golden store loads, mutates and round-trips — every
collection, not just Tasks — and give the domain its first real `IDayShapeReader`.

**Architecture:** one codec per file kind, all sharing one primitives module; `JsonStore` composes
them into a memory-authoritative view swapped by reference on write, behind one global write lock,
with ordered whole-file atomic writes. The startup sequence (assert → snapshot → migrate → sweep →
serve) lands here because migration and snapshots are store facts, not application ones.

**Tech Stack:** .NET 10, `System.Text.Json` (`Utf8JsonWriter` / `JsonDocument`, no serializer
attributes anywhere), xUnit.

**Spec:** `CONTEXT.md` (read by line range via `CONTEXT-INDEX.md`) plus `docs/adr/` — ADR-0001
above all. **On-disk contract:** `tests/TaskGuide.Storage.Tests/fixtures/data/`, the golden store.

**Test list:** `tests/TEST-INVENTORY.md` § "Sequential · `TaskGuide.Storage.Tests`" — 27 lines,
11 already green. Tasks 1, 11 and 12 add lines beyond the inventory; each appends them in its own
commit.

**Branch:** work on `main` (46 commits ahead of `origin/main`, unpushed). Never push.

---

## Global Constraints

These bind every task. A reviewer checks each one.

1. **TDD, red first.** Write the test, run it, watch it fail *for the right reason*. The report
   quotes the verbatim red output and the verbatim green output.
   **Standing ruling, do not re-litigate:** a `NotImplementedException` thrown from an existing
   stub member IS correct red-first evidence in this codebase. The infrastructure shipped as
   signatures that throw; a test that reaches the stub and gets that exception has proven it
   exercises the unimplemented member.
2. **A test that passes against a broken implementation is worth less than no test.** Mutation
   testing is the load-bearing practice here, not red-green: in the last phase five of seven fix
   rounds were for tests that could not fail. For every non-trivial assertion, **apply a specific
   mutation to the implementation, show it red, revert** — and name the mutation in the report.
3. **Test names come verbatim from `tests/TEST-INVENTORY.md`**, snake_cased into C# method names
   (see `tests/TaskGuide.Storage.Tests/JsonStoreTests.cs`). Any test beyond the inventory gets a
   new inventory line appended **in the same commit**.
4. **`CONTEXT.md` wins over the inventory** wherever the inventory paraphrases.
5. **Never read `CONTEXT.md` whole** — 122 KB. Read only the `sed -n 'A,Bp'` ranges your task names.
6. **Facts stored, everything else derived.** Status, Opportunities, Orphan-ness and `Unused` are
   computed on read and never persisted. No `status` field reaches disk, ever (ADR-0007, #47).
7. **New behaviour is a new rule, not configuration.** No settings file, no knob, no tunable.
   `patterns.json`'s `activePatternId` is the only singleton fact in the store and it already has
   a home; do not invent a second one.
8. **Stay in your file lane.** Touch only the files your task names, plus your own new test file.
   If your lane genuinely needs a change in another lane's file, **report it rather than making
   it** — a temporary out-of-lane edit is what a concurrent lane cannot see coming.
9. **The golden store is the on-disk contract.** Every codec must round-trip
   `tests/TaskGuide.Storage.Tests/fixtures/data/` byte-meaningfully (`JsonNode.DeepEquals`, as
   `Tasks_json_round_trips_with_no_status_field` already does). Do not edit fixture files to make
   a codec pass. If a fixture looks wrong, report it.
10. **Three date/time encodings, no overlap** (fixture README): authored clock times `"17:30"`;
    calendar dates `"2026-08-15"`; recorded instants `"2026-08-15T22:45:03Z"`. A codec that writes
    an instant where the contract says a clock time is a defect even if it round-trips.
11. **Unknown fields are preserved, not dropped**, per collection — that is what keeps a rollback
    lossless (`IStartupSequence.MigrateAsync`'s remark). `TaskCodec` already does this per Task;
    every new codec follows the same shape.
12. **Timezone is `America/Chicago`, always, via `DayBoundary.ZoneId`.** Nothing may disagree
    about what day it is. No `TimeZoneInfo` constructed inside a domain function — `now` and the
    boundary are parameters.
13. **Reading a day's shape must never write one.** The Pattern stays unreified; only Overrides
    and Events are stored dated records.
14. **Build with `dotnet test tests/TaskGuide.Storage.Tests` for speed; the whole suite
    (`dotnet test`) must be green before you commit.** You inherit **195** passing tests.
15. `JsonStoreTests.A_read_never_blocks_on_a_write_and_never_sees_a_torn_view` is a known
    best-effort timing test that flakes. Red on that test **alone** is not a failure — re-run once.
    The `chmod`-based health tests are POSIX-only and pass vacuously as root.
16. **Commit your own work** — one commit per lane, message naming the inventory section.
    **Never push.**
17. **Do not dispatch subagents.** Review arrives from the controller.
18. **Write your report file before you finish, not as an afterthought.** The dispatch names the
    path.

## Existing conventions

- xUnit, `[Fact]`/`[Theory]`, `Assert.*`. Test methods are `Snake_case_sentences`.
- One test class per lane, `sealed class`, XML doc naming the inventory section it covers.
- Namespace `TaskGuide.Storage.Tests`; codecs in `TaskGuide.Infrastructure.Storage`.
- Records everywhere; expression-bodied members preferred.
- Codecs are `public static class XCodec` with `Read(string json)` / `Write(Utf8JsonWriter, …)`,
  exactly as `TaskCodec` is. No `JsonSerializer`, no attributes, no source generators.
- Temp dirs via `Directory.CreateTempSubdirectory("taskguide-storage-tests-")`, deleted in
  `Dispose()`. Repo root found by walking up to `task-guide.slnx` (copy `FindRepoRoot` — it is
  four lines, and cross-test-class sharing is not worth a helper assembly).

## File structure

| File | Responsibility | Task |
|---|---|---|
| `Storage/CodecPrimitives.cs` | dates, clock times, instants, `TagSet`, `Offset`, unknown-field capture | 2 |
| `Storage/DayTemplateCodec.cs` | `day-templates.json`, incl. `AvailabilityWindow` + `EventPrototype` | 2 |
| `Storage/PatternCodec.cs` | `patterns.json` (the `PatternBook` envelope) | 3 |
| `Storage/OverrideCodec.cs` | `overrides.json`, incl. `DayTemplateUse` | 3 |
| `Storage/EventCodec.cs` | `events.json` and `event-exceptions.json` | 4 |
| `Storage/CompletionCodec.cs` | `completions/<taskId>.json` and `completions/derived.json` | 5 |
| `Storage/FireCodec.cs` | `fires/<date>.json` | 6 |
| `Storage/ManifestCodec.cs` | `manifest.json` | 8 |
| `Storage/JsonStore.cs` | full load, full `StoreView`, ordered multi-file atomic writes | 7 |
| `Application/Ports/StoreWrites.cs` | the typed write payloads a `StoreMutation` carries | 7 |
| `Storage/SnapshotWriter.cs` | `/data/snapshots/<utc>/`, last 5 | 8 |
| `Storage/StoreMigrations.cs` | ordered N→N+1 steps | 8 |
| `Storage/StartupSequence.cs` | `IStartupSequence`: assert → snapshot → migrate → sweep | 8 |
| `Storage/FireRetention.cs` | 30-day unlink of whole fire files | 9 |
| `Storage/DayShapeReader.cs` | `IDayShapeReader` over the store | 11 |
| `Ids/UlidIdMinter.cs` | the five remaining minters | 1 |
| `Domain/Schedule/DayTemplate.cs` | `DayTemplateLifecycle.IsUnused` | 10 |
| `Domain/Schedule/DateOverride.cs` | `OverrideSpanRequest.Dates()` | 10 |

## The standard lane cycle

Every task below runs this cycle. Where a task says "run the standard lane cycle", these are the
steps — they are written out here once rather than repeated per lane, and each lane adds its own
specific tests, reads and mutation checks on top.

1. Write **one** failing test from the lane's list.
2. Run it and read the failure. It must fail *for the right reason* — an assertion failure, or a
   `NotImplementedException` from the stub you are about to fill (Constraint 1). A **compile
   error is not the red you want**: stub the new type with throwing members first, so the first
   real run fails inside the code under test. Quote the verbatim red output.
3. Write the minimal implementation. Run the test. Quote the verbatim green output.
4. Repeat 1–3 for each remaining test in the lane's list.
5. Run the lane's **mutation checks** (Constraint 2): apply the named mutation, confirm the test
   goes red, revert, and confirm green again. Name each mutation and its result in the report.
6. Run `dotnet test` — the whole suite, not just your project. 195 inherited tests plus yours.
7. Append any beyond-inventory test lines to `tests/TEST-INVENTORY.md`.
8. Write your report file, then commit. Never push.

---

# Wave 1 — independent; codecs, no shared state

## Task 1: The five remaining id minters

**Files:**
- Modify: `src/TaskGuide.Infrastructure/Ids/UlidIdMinter.cs:19-23`
- Create: `tests/TaskGuide.Storage.Tests/UlidIdMinterTests.cs`
- Modify: `tests/TEST-INVENTORY.md` (append the four lines below to the Storage section)

**Read:** `src/TaskGuide.Domain/Common/Ids.cs` (the `IPrefixedId` contract and every prefix).
No `CONTEXT.md` range needed; #23's ULID shape is already in the `UlidIdMinter` XML doc.

**Interfaces:**
- Consumes: `IIdMinter`, `UlidIdMinter.NewUlid()` (already `internal static`).
- Produces: `NextWindowId`, `NextDayTemplateId`, `NextPatternId`, `NextEventId`,
  `NextEventPrototypeId` — each `new(X.Prefix + NewUlid())`, mirroring `NextTaskId` exactly.

**Tests — beyond the inventory, so append these four lines to it in the same commit:**

- every minted id carries its type's prefix and 26 Crockford Base32 characters
- ids minted in sequence sort lexicographically in mint order
- two ids minted in the same millisecond still differ
- a minted id is accepted by its own `IPrefixedId` record struct round-trip

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Every_minted_id_carries_its_types_prefix_and_26_crockford_base32_characters()
{
    var minter = new UlidIdMinter();

    Assert.All(new (string Prefix, string Value)[]
    {
        (WindowId.Prefix, minter.NextWindowId().Value),
        (DayTemplateId.Prefix, minter.NextDayTemplateId().Value),
        (PatternId.Prefix, minter.NextPatternId().Value),
        (EventId.Prefix, minter.NextEventId().Value),
        (EventPrototypeId.Prefix, minter.NextEventPrototypeId().Value),
    }, minted =>
    {
        Assert.StartsWith(minted.Prefix, minted.Value, StringComparison.Ordinal);
        var body = minted.Value[minted.Prefix.Length..];
        Assert.Equal(26, body.Length);
        Assert.All(body, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
    });
}
```

Write the remaining three the same way. For lexicographic order, mint ~50 of one kind with a
1 ms `Thread.Sleep` between batches and assert the list equals its own ordinal sort. For the
same-millisecond test, mint 1,000 in a tight loop and assert `Distinct().Count()` is 1,000.

- [ ] **Step 2: Run and watch each fail**

`dotnet test tests/TaskGuide.Storage.Tests --filter UlidIdMinterTests`
Expected: `NotImplementedException` from the stub (Constraint 1 — this is the right red).

- [ ] **Step 3: Implement** — five expression-bodied members, exactly as `NextTaskId`.

- [ ] **Step 4: Mutation check (Constraint 2).** Change `NextWindowId` to use `DayTemplateId.Prefix`;
      confirm the prefix test goes red; revert. Report the mutation.

- [ ] **Step 5: Run the whole suite, then commit**

```bash
dotnet test
git add src/TaskGuide.Infrastructure/Ids/UlidIdMinter.cs tests/TaskGuide.Storage.Tests/UlidIdMinterTests.cs tests/TEST-INVENTORY.md
git commit -m "Mint the four remaining prefixed ULID kinds"
```

## Task 2: Codec primitives, and the Day template codec

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/CodecPrimitives.cs`
- Create: `src/TaskGuide.Infrastructure/Storage/DayTemplateCodec.cs`
- Modify: `src/TaskGuide.Infrastructure/Storage/TaskCodec.cs` — delete its private date/instant/
  `TagSet`/`Offset` helpers and delegate to `CodecPrimitives`. **Behaviour must not change**; the
  11 existing storage tests are the gate and must stay green with no edit.
- Create: `tests/TaskGuide.Storage.Tests/DayTemplateCodecTests.cs`

**Read:** `sed -n '635,685p' CONTEXT.md` (Day template), `sed -n '589,634p' CONTEXT.md`
(Availability Window). `tests/TaskGuide.Storage.Tests/fixtures/data/day-templates.json`.

**Interfaces:**
- Produces, and **later tasks depend on these exact names**:

```csharp
public static class CodecPrimitives
{
    public static DateOnly ReadDate(JsonElement e);
    public static DateOnly? ReadDateOrNull(JsonElement parent, string property);
    public static void WriteDateOrNull(Utf8JsonWriter w, string property, DateOnly? value);

    public static TimeOnly ReadClockTime(JsonElement e);            // "17:30"
    public static TimeOnly? ReadClockTimeOrNull(JsonElement parent, string property);
    public static void WriteClockTime(Utf8JsonWriter w, string property, TimeOnly value);
    public static void WriteClockTimeOrNull(Utf8JsonWriter w, string property, TimeOnly? value);

    public static DateTimeOffset ReadInstant(JsonElement e);        // "…T22:45:03Z"
    public static DateTimeOffset? ReadInstantOrNull(JsonElement parent, string property);
    public static void WriteInstant(Utf8JsonWriter w, string property, DateTimeOffset value);
    public static void WriteInstantOrNull(Utf8JsonWriter w, string property, DateTimeOffset? value);

    public static TagSet ReadTagSet(JsonElement e);                 // "dimensions" + "looseTags"
    public static void WriteTagSet(Utf8JsonWriter w, TagSet tags);  // writes BOTH properties

    public static Offset? ReadOffsetOrNull(JsonElement parent, string property);
    public static void WriteOffsetOrNull(Utf8JsonWriter w, string property, Offset? offset);

    public static IReadOnlyList<KeyValuePair<string, JsonElement>> UnknownFields(
        JsonElement e, IReadOnlyCollection<string> knownFields);
    public static void WriteUnknownFields(
        Utf8JsonWriter w, IReadOnlyList<KeyValuePair<string, JsonElement>> extras);

    public static AvailabilityWindow ReadWindow(JsonElement e);
    public static void WriteWindow(Utf8JsonWriter w, AvailabilityWindow window);
}

public static class DayTemplateCodec
{
    public static (IReadOnlyList<DayTemplate> Templates,
                   IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(string json);

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<DayTemplate> templates,
        IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras);
}
```

`ReadWindow` lives in `CodecPrimitives` deliberately: a Window appears in **three** files
(`day-templates.json`, `overrides.json`, and nowhere else — but Task 3 needs it, so it is not this
lane's private helper).

**Note on the ordinal single-value encoding.** `TaskCodec.WriteDimensions` writes a one-element
ordinal axis as a bare string (`"energy": "medium"`) and an array otherwise, deciding via
`KnownDimensions.Default`. The fixture's `day-templates.json` uses the same convention
(`"energy": "medium"` on a Window). Move that logic into `CodecPrimitives.WriteTagSet` unchanged —
do not re-derive it.

**Tests — beyond the inventory; append these to it in the same commit:**

- `day-templates.json` round-trips the golden store unchanged
- a Window's start and end round-trip as authored clock times, never as instants
- an Event prototype's `absenceNotice` Offset round-trips, and a null one stays null
- an unknown field on a Day template survives a load-and-save round trip
- `TaskCodec` delegating to `CodecPrimitives` changes no byte of `tasks.json` *(this one is the
  existing `Tasks_json_round_trips_with_no_status_field` staying green — assert nothing new; say
  so in the report rather than adding a duplicate test)*
- **no codec writes a `status` property, whatever type it would carry** — owed from the last
  phase's review. Today "nothing can write a Status" is asserted *structurally over the domain
  assembly*, so a codec emitting `"status": "active"` as a plain **string** would slip through
  every existing test. Assert it at the codec level: round-trip a `TaskItem` through
  `TaskCodec.Write` and assert the emitted object has no `status` property at all — not that it
  is null, that it is absent. Do the same for every codec this plan adds, as a shared helper in
  `CodecPrimitives`-adjacent test code, and say in the report which codecs it covers.

- [ ] **Step 1: Write the round-trip test first**

```csharp
[Fact]
public void Day_templates_json_round_trips_the_golden_store_unchanged()
{
    var original = FixtureJson("day-templates.json");

    var (templates, extras) = DayTemplateCodec.Read(original);
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer)) DayTemplateCodec.Write(writer, templates, extras);

    buffer.Position = 0;
    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(buffer)));
}
```

- [ ] **Step 2: Run it, watch it fail to compile-then-fail on the assertion.** A compile error is
      not the red you want (Constraint 1) — stub `DayTemplateCodec` with throwing members first so
      the first real run fails inside the codec, and say which it was in the report.

- [ ] **Step 3: Extract `CodecPrimitives` from `TaskCodec`, then implement `DayTemplateCodec`.**

- [ ] **Step 4: Run `dotnet test tests/TaskGuide.Storage.Tests`.** Both the new tests and the 11
      inherited ones must pass. If `TaskCodec` output moved by one byte, the extraction was not
      behaviour-preserving.

- [ ] **Step 5: Mutation checks.** (a) Make `WriteClockTime` emit `"HH:mm:ss"`; the clock-time test
      must go red. (b) Drop `WriteUnknownFields`; the unknown-field test must go red. Revert both.

- [ ] **Step 6: Whole suite, then commit.**

```bash
dotnet test
git add -A src/TaskGuide.Infrastructure/Storage tests/TaskGuide.Storage.Tests tests/TEST-INVENTORY.md
git commit -m "Extract the shared codec primitives and read/write day-templates.json"
```

## Task 3: The Pattern and Override codecs

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/PatternCodec.cs`
- Create: `src/TaskGuide.Infrastructure/Storage/OverrideCodec.cs`
- Create: `tests/TaskGuide.Storage.Tests/ScheduleCodecTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '686,779p' CONTEXT.md` (Pattern, Override). Fixtures `patterns.json`,
`overrides.json`.

**Interfaces:**
- Consumes: `CodecPrimitives.ReadWindow` / `WriteWindow` / `ReadDate` / `WriteDateOrNull` /
  `UnknownFields` / `WriteUnknownFields` from Task 2 — exact signatures above.
- Produces:

```csharp
public static class PatternCodec
{
    public static PatternBook Read(string json);
    public static void Write(Utf8JsonWriter writer, PatternBook book);
}

public static class OverrideCodec
{
    public static (IReadOnlyList<DateOverride> Overrides,
                   IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(string json);

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<DateOverride> overrides,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras);
}
```

`patterns.json` is an **object**, not an array — `{ activePatternId, patterns: [...] }`. A
`Pattern`'s `days` is exactly seven `DayTemplateId`s indexed by `DayOfWeek` (Sunday = 0, matching
`Pattern.this[DayOfWeek]`). **Reject a `days` array whose length is not 7 at read**, named — a
six-element array would silently make `pattern[Saturday]` throw an index error far from the cause.

**Tests — append to the inventory:**

- `patterns.json` round-trips the golden store unchanged
- a Pattern's seven days are indexed by weekday with Sunday first
- a Pattern book whose `days` array is not seven long is rejected at read, naming the Pattern
- `overrides.json` round-trips the golden store unchanged
- **an Override's copy preserves each Window's id** *(inventory line, verbatim)*
- **an Override carries its `used` record with the template name as it was** *(inventory line)*
- a one-off day round-trips with a null `used`

The two verbatim inventory lines are asserted here at the **codec** level (the id and the name
survive the round trip). Task 12 asserts them again at the **store mutation** level, where the copy
is actually made. Both are wanted; neither replaces the other.

- [ ] **Run the standard lane cycle.** Mutation checks: (a) mint a fresh `WindowId` inside
      `OverrideCodec.Read`, confirm the preserves-id test goes red; (b) resolve `used.templateName`
      by looking the id up in `day-templates.json` instead of reading the stored string — confirm
      the name-as-it-was test goes red; revert both. **(b) is the point of the whole use-record
      design; a test that cannot catch it is not pinning the rule.**

- [ ] **Step 7: Commit** — `git commit -m "Read and write patterns.json and overrides.json"`

## Task 4: The Event and Event-exception codecs

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/EventCodec.cs`
- Create: `tests/TaskGuide.Storage.Tests/EventCodecTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '780,863p' CONTEXT.md` (Event, Event exception). Fixtures `events.json`,
`event-exceptions.json`.

**Interfaces:**
- Consumes: `CodecPrimitives` (Task 2), all members above.
- Produces:

```csharp
public static class EventCodec
{
    public static (IReadOnlyList<Event> Events,
                   IReadOnlyDictionary<EventId, IReadOnlyList<KeyValuePair<string, JsonElement>>> Extras)
        Read(string json);
    public static void Write(Utf8JsonWriter writer, IReadOnlyList<Event> events,
        IReadOnlyDictionary<EventId, IReadOnlyList<KeyValuePair<string, JsonElement>>> extras);

    public static IReadOnlyList<EventException> ReadExceptions(string json);
    public static void WriteExceptions(Utf8JsonWriter writer, IReadOnlyList<EventException> exceptions);
}
```

An `EventException` is keyed `(Date, PrototypeId)` and covers **edit as well as delete**: a
`deleted: true` row carries null name/start/end; a `deleted: false` row carries all three. A row
that is `deleted: false` with all three null is meaningless — **reject it at read**, named.

**Tests — append to the inventory:**

- `events.json` round-trips the golden store unchanged
- an Event's loose Tags survive the round trip, and are what a derived-obligation rule reads
- `event-exceptions.json` round-trips both the delete row and the edit row
- an Event exception that is neither a delete nor an edit is rejected at read, naming its date
- an Event's `absenceNotice` round-trips, and a null one stays null

- [ ] **Run the standard lane cycle.** Mutation check: make `ReadExceptions` default `Deleted` to
      `true`; the two-row round-trip test must go red. Commit:
      `git commit -m "Read and write events.json and event-exceptions.json"`

## Task 5: The completion-log codecs

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/CompletionCodec.cs`
- Create: `tests/TaskGuide.Storage.Tests/CompletionCodecTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '47,66p' CONTEXT.md` (Task) and `sed -n '281,338p' CONTEXT.md` (Recurrence) for
what `due` means; `src/TaskGuide.Domain/Tasks/Completion.cs` in full — it is 50 lines and it is the
contract. Fixtures `completions/*.json`.

**Interfaces:**
- Consumes: `CodecPrimitives.ReadDateOrNull`, `WriteDateOrNull`, `ReadInstant`, `WriteInstant`.
- Produces:

```csharp
public static class CompletionCodec
{
    /// <summary>`completions/&lt;taskId&gt;.json` — the id comes from the filename, not the file.</summary>
    public static CompletionLog Read(TaskId taskId, string json);
    public static void Write(Utf8JsonWriter writer, CompletionLog log);

    /// <summary>`completions/derived.json`.</summary>
    public static IReadOnlyList<DerivedCompletionEntry> ReadDerived(string json);
    public static void WriteDerived(Utf8JsonWriter writer, IReadOnlyList<DerivedCompletionEntry> entries);

    /// <summary>`t_01ARZ….json`. The filename IS the key; nothing inside the file repeats it.</summary>
    public static string FileNameFor(TaskId taskId);
}
```

**`due` is a calendar date or null; `done` is always an instant.** A one-off Task's entry carries
its Deadline, or null — `completions/t_…FB3.json` in the fixture is exactly that case, and it is
the one that proves the null is real rather than a missing field.

**Tests — one inventory line, verbatim, plus four to append:**

- **a completion log is not rewritten when its Task's title changes** *(inventory line — assert it
  at the codec level here: the log file's bytes carry no title and no reference to one, so a title
  edit has nothing to rewrite. Task 12 asserts the store-level version.)*
- each completion log round-trips the golden store unchanged
- a one-off Task's entry round-trips a null `due`
- `completions/derived.json` round-trips, keyed on `ruleId` + `triggerId` + `due`
- the Task id comes from the filename, so a log file carries no id of its own

- [ ] **Run the standard lane cycle.** Mutation check: write `due` as an instant instead of a date; the null-
      `due` round-trip must go red. Commit: `git commit -m "Read and write the completion logs"`

## Task 6: The Fire record codec

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/FireCodec.cs`
- Create: `tests/TaskGuide.Storage.Tests/FireCodecTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '1184,1232p' CONTEXT.md` (Fire record) — read it in full, it is the whole spec
for this lane. Fixture `fires/2026-08-15.json`.

**Interfaces:**
- Consumes: `CodecPrimitives` clock-time and instant members.
- Produces:

```csharp
public static class FireCodec
{
    /// <summary>`fires/&lt;date&gt;.json` — the date comes from the filename.</summary>
    public static DayFires Read(DateOnly date, string json);
    public static void Write(Utf8JsonWriter writer, DayFires fires);
    public static string FileNameFor(DateOnly date);   // "2026-08-15.json"
    public static DateOnly? DateFromFileName(string fileName);  // null when it is not a fire file
}
```

`DateFromFileName` exists for Task 9's retention sweep, which must decide what to unlink by
filename alone without parsing the contents.

**Two things this lane must get right, both from `CONTEXT.md`:**
- **Times are instants, not clock times** — `dueAt` and `firedAt`. But `windowStart` / `windowEnd`
  are the Window's span **as it was**, and those are authored clock times. The same file therefore
  carries both encodings, and getting one wrong is exactly the bug the entry warns about.
- **`(date, null, "fallback")` is unique per day** — the fallback row's `windowId` is null and the
  key needs no special case.

**Tests — two inventory lines, verbatim, plus three to append:**

- **a fire row carries the Window's name and span as they were** *(inventory line)*
- **`(date, null, "fallback")` is unique per day** *(inventory line — a second fallback row for the
  same date is rejected at read, named)*
- `fires/2026-08-15.json` round-trips the golden store unchanged
- `dueAt` and `firedAt` round-trip as instants while `windowStart` and `windowEnd` round-trip as
  clock times, in the same file
- a pending Snooze row round-trips with a null `firedAt` and reads `IsPendingSnooze`

- [ ] **Run the standard lane cycle.** Mutation checks: (a) write `windowStart` as an instant — the mixed-
      encoding test must go red; (b) drop the duplicate-fallback guard — the uniqueness test must
      go red. Commit: `git commit -m "Read and write the Fire record"`

---

# Wave 2 — the store itself; strictly after Wave 1

## Task 7: Load and write the whole store

**Files:**
- Modify: `src/TaskGuide.Infrastructure/Storage/JsonStore.cs` (all of it)
- Create: `src/TaskGuide.Application/Ports/StoreWrites.cs`
- Modify: `src/TaskGuide.Application/Ports/IStore.cs` — **doc only**, naming the write payload
  types. Do not change a signature.
- Modify: `tests/TaskGuide.Storage.Tests/JsonStoreTests.cs` — the one call site
  `new StoreMutation([view.Tasks])` becomes `new StoreMutation([new TasksWrite(view.Tasks)])`.
  Change nothing else in that file; the other ten tests must pass untouched.
- Create: `tests/TaskGuide.Storage.Tests/WholeStoreTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '1099,1183p' CONTEXT.md` (Backup — for the crash-consistency and write-order
reasoning), ADR-0001 in full. `src/TaskGuide.Application/Ports/IStore.cs` in full.

**Interfaces:**
- Consumes: every codec from Wave 1, by the exact names above.
- Produces:

```csharp
// StoreWrites.cs — one record per file kind a mutation can touch. `StoreMutation.OrderedWrites`
// carries these, in the order they must hit disk.
public sealed record TasksWrite(IReadOnlyList<TaskItem> Tasks);
public sealed record DayTemplatesWrite(IReadOnlyList<DayTemplate> Templates);
public sealed record PatternsWrite(PatternBook Book);
public sealed record OverridesWrite(IReadOnlyList<DateOverride> Overrides);
public sealed record EventsWrite(IReadOnlyList<Event> Events);
public sealed record EventExceptionsWrite(IReadOnlyList<EventException> Exceptions);
public sealed record CompletionLogWrite(CompletionLog Log);
public sealed record DerivedCompletionsWrite(IReadOnlyList<DerivedCompletionEntry> Entries);
public sealed record FiresWrite(DayFires Fires);
```

`StoreView` implements every `IStoreView` member for real; nothing throws
`NotImplementedException` when this lane is done. `MutateAsync` applies each write **in list
order**, each one atomic on its own, and swaps `_current` only after the last succeeds. A write
that throws part-way leaves the earlier files written — **that is the accepted design**, not a bug
to engineer away; `LastWriteSucceeded` goes false and the view is not swapped.

**Tests — three inventory lines, verbatim, plus four to append:**

- **the whole store loads into typed objects at startup** *(the existing test, widened from Tasks
  to every collection — edit it in place rather than adding a second)*
- **an Event-plus-Override write puts the Event first** *(inventory line)*
- **a crash between the two leaves the state the overlap check detects, and the next read re-offers
  the prompt** *(inventory line)*
- a mutation writes every affected file before the request returns, not only the first
- a partially-failed multi-file write leaves `LastWriteSucceeded` false and does not swap the view
- a missing collection file loads as empty rather than throwing — a fresh `/data` is valid
- a corrupt collection file fails at registration, not first use *(the existing
  `AddJsonStore_loads_eagerly…` rule, extended past `tasks.json`)*

For the crash test: do not kill a process. Inject the failure by making the **second** write's path
unwritable (`chmod`) — the existing atomicity test already uses that technique; follow it.
Constraint 15's POSIX caveat applies, so guard with `if (OperatingSystem.IsWindows()) return;` and
say so in the report.

- [ ] **Step 1:** widen `The_whole_store_loads_into_typed_objects_at_startup` to assert against
      every collection of the golden fixture, copied whole into the temp dir. Run it: red on
      `NotImplementedException` from `StoreView`.
- [ ] **Step 2:** implement the full `Load` and `StoreView`.
- [ ] **Step 3:** write the Event-first test, run it red, then implement ordered multi-file writes.
- [ ] **Step 4:** mutation checks — (a) reverse `OrderedWrites` before applying; the Event-first
      test must go red. (b) swap `_current` before the writes rather than after; the partial-failure
      test must go red. Revert both.
- [ ] **Step 5:** whole suite, then commit.
      `git commit -m "Load and write every collection in the store"`

## Task 8: manifest, snapshots, migration, and the startup sequence

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/ManifestCodec.cs`
- Create: `src/TaskGuide.Infrastructure/Storage/SnapshotWriter.cs`
- Create: `src/TaskGuide.Infrastructure/Storage/StoreMigrations.cs`
- Create: `src/TaskGuide.Infrastructure/Storage/StartupSequence.cs`
- Modify: `src/TaskGuide.Infrastructure/Storage/ServiceCollectionExtensions.cs` — register
  `IStartupSequence`
- Create: `tests/TaskGuide.Storage.Tests/StartupSequenceTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `src/TaskGuide.Application/Ports/IStartupSequence.cs` in full — the XML docs are the
spec. `sed -n '1099,1140p' CONTEXT.md` (Backup, for the Snapshot-vs-Backup distinction). ADR-0001.

**Interfaces:**
- Consumes: `JsonStore` (Task 7), `DimensionRegistry.Claiming` and `RegistrySweep` (already
  implemented in the domain), every codec.
- Produces:

```csharp
public static class ManifestCodec
{
    public const int CurrentVersion = 1;              // what THIS binary writes
    public static int Read(string json);              // `{ "version": 1 }`
    public static void Write(Utf8JsonWriter writer, int version);
}

public sealed class SnapshotWriter
{
    public SnapshotWriter(string dataDir);
    /// <summary>`/data/snapshots/&lt;utc&gt;/`, whole-file copies, keeping the last 5.</summary>
    public Task<string> TakeAsync(IReadOnlyList<string> relativePaths, DateTimeOffset now, CancellationToken ct);
}

public static class StoreMigrations
{
    /// <summary>Ordered N→N+1 steps. Empty today — version 1 is the first version.</summary>
    public static IReadOnlyList<StoreMigration> Ordered { get; }
}

public sealed record StoreMigration(int From, int To, Func<string, CancellationToken, Task> Apply);

public sealed class StartupSequence : IStartupSequence { /* assert → snapshot → migrate → sweep */ }
```

**`StoreMigrations.Ordered` is empty today and that is correct** — version 1 is the only version
that has existed. Do **not** invent a fake version 2 in production code to have something to run.
The test supplies its own ordered list through a constructor parameter; that is what makes the
N→N+1 walk testable without lying in `src/`.

**Three inventory lines, verbatim, plus four to append:**

- **`manifest.json` version mismatch runs the ordered N→N+1 steps at startup** *(inventory line)*
- **a snapshot is written once per startup, and only when that startup will write** *(inventory line
  — the load-bearing half is "only when it will write": a startup with nothing to migrate and
  nothing to demote takes no snapshot at all)*
- **snapshots keep the last 5** *(inventory line)*
- a store already at `CurrentVersion` runs no migration step and takes no snapshot
- a version **ahead** of this binary refuses to start, named — a rollback must not silently
  down-migrate
- a registry collision signals outbound before exiting, and no snapshot is taken
- `manifest.json` is written only after every migration step succeeds

- [ ] **Run the standard lane cycle.** Mutation checks: (a) take the snapshot unconditionally; the
      only-when-it-will-write test must go red. (b) keep 6 snapshots; the last-5 test must go red.
      (c) run migrations newest-first; the ordered-walk test must go red. Revert each.
- [ ] **Commit:** `git commit -m "assert → snapshot → migrate → sweep at startup"`

## Task 9: Fire-record retention

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/FireRetention.cs`
- Create: `tests/TaskGuide.Storage.Tests/FireRetentionTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '1184,1232p' CONTEXT.md` (Fire record — the retention bullet).

**Interfaces:**
- Consumes: `FireCodec.DateFromFileName` (Task 6).
- Produces: `public static class FireRetention { public static int Sweep(string dataDir, DateOnly today); }`
  — returns the number of files unlinked, which Liveness reads as its **write health** signal
  (Constraint: "write health is read off the retention sweep's outcome, not a probe").

**30 days, by whole files, one `rm` each.** A file whose name does not parse as a date is left
alone, not deleted — the sweep must never be the thing that eats an unexpected file.

**One inventory line, verbatim, plus three to append:**

- **fires older than 30 days are unlinked as whole files** *(inventory line)*
- a fire file exactly 30 days old is kept (the boundary must not drift)
- a file in `fires/` whose name is not a date is left untouched
- the sweep on an absent `fires/` directory is a no-op, not an error

- [ ] **Run the standard lane cycle.** Mutation check: change `>` to `>=` at the boundary; the exactly-30-days test
      must go red. Commit: `git commit -m "Unlink fire records older than 30 days"`

---

# Wave 3 — behaviour over the store; strictly after Wave 2

## Task 10: `Unused`, and the Override span gesture

**Files:**
- Modify: `src/TaskGuide.Domain/Schedule/DayTemplate.cs:44-49` (`DayTemplateLifecycle.IsUnused`)
- Modify: `src/TaskGuide.Domain/Schedule/DateOverride.cs:61` (`OverrideSpanRequest.Dates()`)
- Create: `tests/TaskGuide.Domain.Tests/DayTemplateLifecycleTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**This lane's tests live in `TaskGuide.Domain.Tests`, not the Storage project** — both members are
pure functions over lists the store supplies. Run `dotnet test tests/TaskGuide.Domain.Tests`.

**Read:** `sed -n '635,685p' CONTEXT.md` (Day template — the `Unused` section is the spec, read it
twice), `sed -n '712,779p' CONTEXT.md` (Override).

**Interfaces:**
- Produces: `DayTemplateLifecycle.IsUnused(DayTemplateId, IReadOnlyList<Pattern> allPatterns,
  IReadOnlyList<DateOverride> overrides, DateOnly today)` — signature already declared, do not
  change it. `OverrideSpanRequest.Dates()` yields every date from `From` to `To` **inclusive**.

**Both clauses of `Unused` are load-bearing**, and the second is where this goes wrong: ±13 months
is **symmetric**, so a December already stamped counts as strongly as a December past. The
threshold is **one** use.

**Three inventory lines, verbatim, plus four to append:**

- **`Unused` is false for a template referenced only by a dormant Pattern** *(inventory line)*
- **`Unused` is false for a template stamped within ±13 months, in either direction** *(inventory
  line — two tests, one each direction, and say so when appending)*
- **deleting an `Unused` template corrupts no record** *(inventory line — an `Unused` template is
  reachable from nothing: no Pattern names it and every Override holds a copy, so assert that
  removing it from the list leaves every Override's Windows byte-identical)*
- a template stamped 14 months ago is `Unused`
- a template stamped 14 months ahead is `Unused`
- an Override span of one date yields exactly that date
- an Override span yields every date inclusive of both ends, in ascending order

- [ ] **Run the standard lane cycle.** Mutation checks: (a) make the horizon one-directional (past only); the
      forward-direction test must go red. (b) count only the **active** Pattern; the dormant test
      must go red. (c) make `Dates()` exclusive of `To`; the inclusive test must go red. Revert.
- [ ] **Commit:** `git commit -m "Derive Unused, and expand an Override span to its dates"`

## Task 11: `IDayShapeReader`

**Files:**
- Create: `src/TaskGuide.Infrastructure/Storage/DayShapeReader.cs`
- Create: `tests/TaskGuide.Storage.Tests/DayShapeReaderTests.cs`
- Modify: `tests/TEST-INVENTORY.md`

**Read:** `sed -n '686,779p' CONTEXT.md` (Pattern, Override), `sed -n '780,863p' CONTEXT.md`
(Event, Event exception), `src/TaskGuide.Domain/Schedule/DayShape.cs` in full.

**This is the largest untested joint in the merged work.** Ranking, Opportunities and the absence
rule all consume `IDayShapeReader`, and until now it has existed only as test fakes. Every seam
they were proven against is unproven against real data until this lane lands.

**Interfaces:**
- Consumes: `IStore.Read()` (Task 7), `PatternBook.Active`, `Pattern.this[DayOfWeek]`.
- Produces: `public sealed class DayShapeReader(IStore store) : IDayShapeReader` with
  `DayShape For(DateOnly date)`.

**The rule, in order:**
1. Windows: `Override[date] ?? DayTemplates[activePattern[date.DayOfWeek]]`. `IsOverridden` is
   true exactly when an Override exists for the date — **including an Override with zero
   Windows**, which is a real authored shape ("Travel day"), not an absence.
2. Events: every `Event` whose `Date` is this date, **plus** the recurring instances the active
   Pattern's template generates from its `EventPrototype`s for this weekday.
3. Exceptions apply to the recurring instances only, keyed `(date, prototypeId)`: `Deleted` drops
   the instance; otherwise the non-null `Name`/`Start`/`End` replace the prototype's.
4. **Reading never writes.** No Override is materialised, no file is touched, `IStore.MutateAsync`
   is never called. Assert this directly — a spy store whose `MutateAsync` fails the test if
   reached.

**A recurring instance's `EventId` must be deterministic for the date** — derive it from the
prototype id and the date rather than minting. Minting on read would give the same day two
different ids on two reads, and a mid-day materialised Override would then read as a different
Event. Report the encoding you chose; it is a design decision this plan deliberately leaves to the
implementer, and the reviewer should judge it on its merits.

**Tests — all beyond the inventory; append them:**

- a date with no Override takes the active Pattern's template for its weekday
- a date with an Override takes the Override's Windows and reads `IsOverridden`
- an Override with zero Windows is a shape, not an absence — `IsOverridden` is true and the
  Pattern's Windows do not leak through
- a dated Event on the date appears in the shape
- a recurring instance from the weekday's Event prototype appears in the shape
- a deleted instance's Event exception drops it
- an edited instance's Event exception replaces its name and span, leaving the prototype untouched
- an Event exception for a different prototype on the same date changes nothing
- **reading a day's shape writes nothing** — no Override is materialised and `MutateAsync` is
  never called
- a recurring instance's Event id is the same on two reads of the same date

- [ ] **Run the standard lane cycle**, one test at a time. Mutation checks: (a) treat a zero-Window Override as
      absent and fall through to the Pattern; the zero-Windows test must go red. (b) apply an
      exception by prototype id alone, ignoring the date; the different-date test must go red.
      (c) mint a fresh `EventId` per read; the stable-id test must go red. Revert each.
- [ ] **Commit:** `git commit -m "Read a day's shape from the store without writing one"`

## Task 12: The mutation-level rules, and the restore failure mode

**Files:**
- Create: `tests/TaskGuide.Storage.Tests/StoreMutationRulesTests.cs`
- Modify: `src/TaskGuide.Infrastructure/Storage/JsonStore.cs` only if a test proves a gap
- Modify: `tests/TEST-INVENTORY.md` if any line needs adding

**Read:** `sed -n '712,779p' CONTEXT.md` (Override — the stamp, the use record, promotion),
`sed -n '1099,1183p' CONTEXT.md` (Backup — the restore section is the spec for the last test).

This lane writes **no new production abstraction**. It asserts, against the real store, the rules
the earlier lanes made possible. If a test cannot be written without new production code, that is a
finding to report, not a licence to design.

**Six inventory lines, verbatim, and every one of them stays exactly as written:**

- **a date materialised mid-day does not re-fire an already-fired Window** — stamp an Override onto
  today after a Window has fired, then assert the fire row for `(date, windowId)` still matches,
  because the copy preserved the id. This is *the* reason ids are preserved; without this test the
  rule is asserted only at the codec level, where nothing is actually copied.
- **the use record survives the date becoming a one-off day**
- **re-stamping replaces the use record rather than appending**
- **promoting a one-off day writes the source date's use record and does not re-link** — after
  promotion the source date still holds its own copied Windows, and a later edit to the new
  template does not reach them
- **a completion log is not rewritten when its Task's title changes** — mutate the title, assert
  the completion file's mtime and bytes are unchanged
- **a restore under a running service is invisible, and the next mutation destroys it** — the one
  test that documents a failure mode rather than preventing it. Overwrite `tasks.json` on disk
  under a live `JsonStore`, assert `Read()` still shows the old state, then mutate and assert the
  restored bytes are gone. **Do not "fix" this.** It falls straight out of memory-authoritative
  storage and it is why #49's restore drill exists.

- [ ] **Run the standard lane cycle.** For the restore test the mutation check is inverted — make `Read()`
      re-read the file from disk; the test must go red, because the failure mode it documents would
      no longer exist. Revert, and note in the report that this is the one lane where a green test
      encodes a limitation rather than a guarantee.
- [ ] **Commit:** `git commit -m "Pin the store's mutation-level rules and the restore failure mode"`

---

## Out of scope for this plan — do not drift into it

- **The tick loop, notifications, weather, Glance, capture, Receipt, Liveness.** 63 inventory lines
  in `TaskGuide.Application.Tests`; a separate plan, written after this one lands.
- **Uncommenting `Program.cs`'s three lines.** `AddTaskGuideDomain()` does not exist and this plan
  does not write it. The startup sequence Task 8 builds is registered but not yet called from
  `Program.cs`; wiring it is the next plan's first task, once there is a domain registration to
  order it against.
- **#49's restore drill** — ops work on pi5, not code. Task 12 writes the test that documents the
  failure mode; the drill itself proves the Backup works, and is the user's to schedule.
- **`Notifications/Glance.cs`, `Reminder.cs`, `TimeToLivePolicy.For`** — need notification context,
  not storage.

## Carried forward — constraints on the NEXT plan, recorded here so they are not lost

- **`UrgencyBand` must derive from the same horizon rule `OpportunityCounter.HorizonEnd` uses**,
  and something must map a Duration `TagValue` to `RankKey.DurationRankDescending`. Computed twice,
  they drift, and ADR-0004's "the bands invent no thresholds" is exactly that guarantee.
- **A zero-length Window (the spring gap) must never reach matching.** `DurationCeiling` falls back
  to the smallest bucket below 2 minutes, which is only safe because the engine excludes such a
  Window first. Nothing enforces it yet.
- **Never compute a Pattern-week count for an `Unprocessed` Task** — `Matcher` throws on the null
  Duration by design (ADR-0007). Currently documented only in a test comment.
- **`CountAhead` has no fetched-values parameter.** 0 is the *floor* of the Scarcity key, so a
  weather-tagged Task outranks everything in its band, permanently. Blocking on key assembly.
- **`StatusRules.Of` is O(instances since `CreatedAt`)** and calls the generator ~4×. A decade-old
  daily Task is ~3,650 forward steps per eligibility check, every ~30 s.
- **`OrdinalDimension.RankOf` returns `-1` for an unknown value**, which silently means "fits
  everything". `RegistrySweep` prevents it; the sweep is wired to startup by Task 8 here, so this
  closes once Task 8 is called from `Program.cs`.
- **`DayBoundary.StartOf(date)` does not exist**; two lanes spell it `EndOf(date.AddDays(-1))`.
- **`Event` carries no `EventPrototypeId? PrototypeId`.** A recurring instance is matched to its
  prototype by name, so two same-named Events on one date conflate. Task 11 above works around it
  with a derived id; if that proves awkward, the modelling fix is the honest answer.

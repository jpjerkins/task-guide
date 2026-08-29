using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// The <see cref="IStore"/> substrate for the walking skeleton (#51): memory-authoritative,
/// one global write lock, atomic whole-file writes to a bind mount. <c>Load</c> reads every file
/// named in the golden store fixture — <c>tasks.json</c>, <c>day-templates.json</c>,
/// <c>patterns.json</c>, <c>overrides.json</c>, <c>events.json</c>,
/// <c>event-exceptions.json</c>, every <c>completions/&lt;taskId&gt;.json</c> plus
/// <c>completions/derived.json</c>, and every <c>fires/&lt;date&gt;.json</c> — into a fully
/// populated <see cref="StoreView"/>; a missing file loads as the empty collection and a corrupt
/// one throws here, at construction. <see cref="MutateAsync"/> writes every collection: a
/// <see cref="StoreMutation"/> carries one payload per file kind (<see cref="TasksWrite"/> and the
/// rest of `StoreWrites.cs`), applied in list order, each atomic on its own.
/// </summary>
public sealed class JsonStore : IStore
{
    private readonly string _dataDir;
    private readonly string _tasksPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile StoreView _current;

    public JsonStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _dataDir = dataDir;
        _tasksPath = Path.Combine(dataDir, "tasks.json");
        _current = Load(dataDir);
    }

    /// <summary>
    /// Loads the whole store per ADR-0001: every collection eagerly, wholly, at startup. A
    /// missing collection file is a valid, fresh `/data` and loads as the empty collection — a
    /// corrupt one throws here, at registration, never lazily on first use.
    /// </summary>
    private static StoreView Load(string dataDir)
    {
        IReadOnlyList<TaskItem> tasks = File.Exists(Path.Combine(dataDir, "tasks.json"))
            ? TaskCodec.Read(File.ReadAllText(Path.Combine(dataDir, "tasks.json")))
            : [];

        IReadOnlyList<DayTemplate> dayTemplates = File.Exists(Path.Combine(dataDir, "day-templates.json"))
            ? DayTemplateCodec.Read(File.ReadAllText(Path.Combine(dataDir, "day-templates.json")))
            : [];

        var patternsPath = Path.Combine(dataDir, "patterns.json");
        var patterns = File.Exists(patternsPath)
            ? PatternCodec.Read(File.ReadAllText(patternsPath))
            : new PatternBook(default, []);

        IReadOnlyList<DateOverride> overrides = File.Exists(Path.Combine(dataDir, "overrides.json"))
            ? OverrideCodec.Read(File.ReadAllText(Path.Combine(dataDir, "overrides.json")))
            : [];

        IReadOnlyList<Event> events = File.Exists(Path.Combine(dataDir, "events.json"))
            ? EventCodec.Read(File.ReadAllText(Path.Combine(dataDir, "events.json")))
            : [];

        var eventExceptionsPath = Path.Combine(dataDir, "event-exceptions.json");
        IReadOnlyList<EventException> eventExceptions = File.Exists(eventExceptionsPath)
            ? EventCodec.ReadExceptions(File.ReadAllText(eventExceptionsPath))
            : [];

        var (completionLogs, derivedCompletions) = LoadCompletions(dataDir);
        var fires = LoadFires(dataDir);

        return new StoreView(
            tasks,
            dayTemplates,
            patterns,
            overrides,
            events,
            eventExceptions,
            completionLogs,
            derivedCompletions,
            fires);
    }

    /// <summary>
    /// `completions/` holds one log per Task (filename is the TaskId, per
    /// <see cref="CompletionCodec.FileNameFor"/>) plus one envelope, `derived.json`, excluded
    /// from the per-Task scan by name.
    /// </summary>
    private static (
        IReadOnlyDictionary<TaskId, CompletionLog> Logs,
        IReadOnlyList<DerivedCompletionEntry> Derived)
        LoadCompletions(string dataDir)
    {
        var completionsDir = Path.Combine(dataDir, "completions");

        var logs = new Dictionary<TaskId, CompletionLog>();
        IReadOnlyList<DerivedCompletionEntry> derived = [];

        if (Directory.Exists(completionsDir))
        {
            var derivedPath = Path.Combine(completionsDir, "derived.json");
            if (File.Exists(derivedPath))
            {
                derived = CompletionCodec.ReadDerived(File.ReadAllText(derivedPath));
            }

            foreach (var path in Directory.EnumerateFiles(completionsDir, "*.json"))
            {
                if (Path.GetFileName(path) == "derived.json") continue;

                var taskId = new TaskId(Path.GetFileNameWithoutExtension(path));
                logs[taskId] = CompletionCodec.Read(taskId, File.ReadAllText(path));
            }
        }

        return (logs, derived);
    }

    /// <summary>`fires/&lt;date&gt;.json` — one file per day; the date comes from the filename.</summary>
    private static IReadOnlyDictionary<DateOnly, DayFires> LoadFires(string dataDir)
    {
        var firesDir = Path.Combine(dataDir, "fires");

        var fires = new Dictionary<DateOnly, DayFires>();

        if (Directory.Exists(firesDir))
        {
            foreach (var path in Directory.EnumerateFiles(firesDir))
            {
                var date = FireCodec.DateFromFileName(Path.GetFileName(path));
                if (date is null) continue;

                fires[date.Value] = FireCodec.Read(date.Value, File.ReadAllText(path));
            }
        }

        return fires;
    }

    /// <summary>Not a copy — the current immutable view, swapped by reference on every successful mutation.</summary>
    public IStoreView Read() => _current;

    // 0 = no write attempted yet, 1 = last write succeeded, 2 = last write failed. An int
    // (not bool?) so it can be updated and read via Interlocked without a lock — Read()-adjacent
    // health checks must never block on the write lock.
    private const int NoWriteYet = 0;
    private const int WriteSucceeded = 1;
    private const int WriteFailed = 2;
    private int _lastWriteOutcome = NoWriteYet;

    public bool? LastWriteSucceeded => Interlocked.CompareExchange(ref _lastWriteOutcome, 0, 0) switch
    {
        WriteSucceeded => true,
        WriteFailed => false,
        _ => null,
    };

    /// <summary>
    /// Applies <see cref="StoreMutation.OrderedWrites"/> in list order, each write atomic on its
    /// own file. Every other collection is carried forward unchanged from the pre-mutation view —
    /// a write touching only some collections must not erase what the rest of the view already
    /// held. A write that throws part-way leaves the earlier files written on disk (the accepted
    /// design — see <see cref="IStore.MutateAsync"/>'s doc comment); <see cref="_current"/> is
    /// swapped only once every write in the list has succeeded.
    /// </summary>
    public async Task MutateAsync(Func<IStoreView, StoreMutation> mutation, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var view = _current;
            var result = mutation(view);

            // Carried forward from `view` and replaced in place, one field at a time, as each
            // write in the list is applied — never all at once, so a write list that only
            // touches some collections leaves the rest exactly as they were.
            var tasks = view.Tasks;
            var dayTemplates = view.DayTemplates;
            var patterns = view.Patterns;
            var overrides = view.Overrides;
            var events = view.Events;
            var eventExceptions = view.EventExceptions;
            var completionLogs = view.CompletionLogs;
            var derivedCompletions = view.DerivedCompletions;
            var fires = view.Fires;

            // Whether any file was actually opened for writing this call. LastWriteSucceeded
            // documents the outcome of the most recent *actual disk write* — an unrecognised
            // payload that throws before anything is attempted is a caller bug, not a disk
            // failure, and must not be reported as one (see the `default:` case below).
            var attemptedWrite = false;

            try
            {
                foreach (var write in result.OrderedWrites)
                {
                    switch (write)
                    {
                        case TasksWrite w:
                            attemptedWrite = true;
                            // A defensive copy: the store must own its storage. `w.Tasks` came
                            // straight out of the caller's StoreMutation and IReadOnlyList<T> is
                            // not a promise of immutability — a caller that keeps its own
                            // reference and mutates it later must not be able to reach a view any
                            // concurrent reader already holds. Every write below makes the same
                            // copy for the same reason.
                            tasks = w.Tasks.ToArray();
                            await WriteAtomicAsync(_tasksPath, writer => TaskCodec.Write(writer, tasks), cancellationToken);
                            break;

                        case DayTemplatesWrite w:
                            attemptedWrite = true;
                            dayTemplates = w.Templates.ToArray();
                            await WriteAtomicAsync(
                                Path.Combine(_dataDir, "day-templates.json"),
                                writer => DayTemplateCodec.Write(writer, dayTemplates),
                                cancellationToken);
                            break;

                        case PatternsWrite w:
                            attemptedWrite = true;
                            patterns = w.Book with { Patterns = w.Book.Patterns.ToArray() };
                            await WriteAtomicAsync(
                                Path.Combine(_dataDir, "patterns.json"),
                                writer => PatternCodec.Write(writer, patterns),
                                cancellationToken);
                            break;

                        case OverridesWrite w:
                            attemptedWrite = true;
                            overrides = w.Overrides.ToArray();
                            await WriteAtomicAsync(
                                Path.Combine(_dataDir, "overrides.json"),
                                writer => OverrideCodec.Write(writer, overrides),
                                cancellationToken);
                            break;

                        case EventsWrite w:
                            attemptedWrite = true;
                            events = w.Events.ToArray();
                            await WriteAtomicAsync(
                                Path.Combine(_dataDir, "events.json"),
                                writer => EventCodec.Write(writer, events),
                                cancellationToken);
                            break;

                        case EventExceptionsWrite w:
                            attemptedWrite = true;
                            eventExceptions = w.Exceptions.ToArray();
                            await WriteAtomicAsync(
                                Path.Combine(_dataDir, "event-exceptions.json"),
                                writer => EventCodec.WriteExceptions(writer, eventExceptions),
                                cancellationToken);
                            break;

                        case CompletionLogWrite w:
                            attemptedWrite = true;
                            {
                                var log = w.Log with { Entries = w.Log.Entries.ToArray() };
                                var updatedLogs = new Dictionary<TaskId, CompletionLog>(completionLogs) { [log.TaskId] = log };
                                completionLogs = updatedLogs;

                                var completionsDir = Path.Combine(_dataDir, "completions");
                                Directory.CreateDirectory(completionsDir);
                                await WriteAtomicAsync(
                                    Path.Combine(completionsDir, CompletionCodec.FileNameFor(log.TaskId)),
                                    writer => CompletionCodec.Write(writer, log),
                                    cancellationToken);
                            }
                            break;

                        case DerivedCompletionsWrite w:
                            attemptedWrite = true;
                            derivedCompletions = w.Entries.ToArray();
                            {
                                var completionsDir = Path.Combine(_dataDir, "completions");
                                Directory.CreateDirectory(completionsDir);
                                await WriteAtomicAsync(
                                    Path.Combine(completionsDir, "derived.json"),
                                    writer => CompletionCodec.WriteDerived(writer, derivedCompletions),
                                    cancellationToken);
                            }
                            break;

                        case FiresWrite w:
                            attemptedWrite = true;
                            {
                                var dayFires = w.Fires with { Rows = w.Fires.Rows.ToArray() };
                                var updatedFires = new Dictionary<DateOnly, DayFires>(fires) { [dayFires.Date] = dayFires };
                                fires = updatedFires;

                                var firesDir = Path.Combine(_dataDir, "fires");
                                Directory.CreateDirectory(firesDir);
                                await WriteAtomicAsync(
                                    Path.Combine(firesDir, FireCodec.FileNameFor(dayFires.Date)),
                                    writer => FireCodec.Write(writer, dayFires),
                                    cancellationToken);
                            }
                            break;

                        default:
                            // An unrecognised payload is a caller/programming bug, not (yet) a
                            // disk failure — `attemptedWrite` is what decides how the catch below
                            // reports it: untouched if this is the first thing in the list (no
                            // real write was ever attempted this call), WriteFailed if an earlier
                            // write in the same list already landed on disk.
                            throw new NotImplementedException($"JsonStore does not know how to write a {write.GetType().Name}.");
                    }
                }
            }
            catch
            {
                // LastWriteSucceeded documents the outcome of an *actual disk write* (see
                // IStore.LastWriteSucceeded's doc comment) — a caller bug that never reached a
                // real write (an unrecognised payload as the very first item) must leave it
                // exactly as an unwritten store leaves it: untouched, not a false "failed".
                if (attemptedWrite)
                {
                    Interlocked.Exchange(ref _lastWriteOutcome, WriteFailed);
                }
                throw;
            }

            _current = new StoreView(
                tasks,
                dayTemplates,
                patterns,
                overrides,
                events,
                eventExceptions,
                completionLogs,
                derivedCompletions,
                fires);

            // Symmetric with the failure path above: an empty OrderedWrites list attempted no
            // real disk write, so LastWriteSucceeded must stay exactly as an unwritten store
            // leaves it — untouched, not a false "succeeded".
            if (attemptedWrite)
            {
                Interlocked.Exchange(ref _lastWriteOutcome, WriteSucceeded);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Write to a temp file in the same directory, fsync it, then rename over the destination.
    /// The rename is atomic on the same filesystem — <paramref name="path"/> is never observable
    /// as a torn or partial write, because nothing touches <paramref name="path"/> itself until
    /// the very last step.
    /// </summary>
    /// <remarks>
    /// Portability caveat: this also fsyncs the file, but not the containing directory — .NET has
    /// no portable API for that (it needs a raw file descriptor and an `fsync` syscall on the
    /// directory, which is POSIX-specific and unavailable through <see cref="System.IO"/>). On a
    /// host that crashes between the rename and the directory entry reaching stable storage, the
    /// rename could theoretically be lost. Accepted for the walking skeleton; worth revisiting if
    /// pi5's storage stack turns out to need it.
    /// </remarks>
    private static async Task WriteAtomicAsync(string path, Action<Utf8JsonWriter> writeContent, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writeContent(writer);
                    await writer.FlushAsync(cancellationToken);
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

internal sealed class StoreView(
    IReadOnlyList<TaskItem> tasks,
    IReadOnlyList<DayTemplate> dayTemplates,
    PatternBook patterns,
    IReadOnlyList<DateOverride> overrides,
    IReadOnlyList<Event> events,
    IReadOnlyList<EventException> eventExceptions,
    IReadOnlyDictionary<TaskId, CompletionLog> completionLogs,
    IReadOnlyList<DerivedCompletionEntry> derivedCompletions,
    IReadOnlyDictionary<DateOnly, DayFires> fires)
    : IStoreView
{
    public IReadOnlyList<TaskItem> Tasks { get; } = tasks;

    public IReadOnlyList<DayTemplate> DayTemplates { get; } = dayTemplates;

    public PatternBook Patterns { get; } = patterns;

    public IReadOnlyList<DateOverride> Overrides { get; } = overrides;

    public IReadOnlyList<Event> Events { get; } = events;

    public IReadOnlyList<EventException> EventExceptions { get; } = eventExceptions;

    /// <summary>Every per-Task completion log that has a file, keyed by TaskId — the filename's inverse (<see cref="CompletionCodec.FileNameFor"/>).</summary>
    internal IReadOnlyDictionary<TaskId, CompletionLog> CompletionLogs { get; } = completionLogs;

    public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions { get; } = derivedCompletions;

    /// <summary>Every day's Fire record that has a file, keyed by date — the filename's inverse (<see cref="FireCodec.FileNameFor"/>).</summary>
    internal IReadOnlyDictionary<DateOnly, DayFires> Fires { get; } = fires;

    /// <summary>An absent log reads as the empty log, never null and never a throw.</summary>
    public CompletionLog CompletionsFor(TaskId task) =>
        CompletionLogs.TryGetValue(task, out var log) ? log : CompletionLog.Empty(task);

    /// <summary>An absent day's Fire record reads as the empty record, never null and never a throw.</summary>
    public DayFires FiresOn(DateOnly date) =>
        Fires.TryGetValue(date, out var dayFires) ? dayFires : new DayFires(date, []);
}

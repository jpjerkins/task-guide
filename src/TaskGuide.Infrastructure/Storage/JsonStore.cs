using System.Text.Json;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tasks;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// The <see cref="IStore"/> substrate for the walking skeleton (#51): memory-authoritative,
/// one global write lock, atomic whole-file writes to a bind mount. Only <c>tasks.json</c> is
/// wired up — every other file named in the golden store fixture is a later ticket, and
/// <see cref="StoreView"/> throws <see cref="NotImplementedException"/> for all of it rather
/// than pretending.
/// </summary>
public sealed class JsonStore : IStore
{
    private readonly string _tasksPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile StoreView _current;

    public JsonStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
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
        var (tasks, taskExtras) = File.Exists(Path.Combine(dataDir, "tasks.json"))
            ? TaskCodec.Read(File.ReadAllText(Path.Combine(dataDir, "tasks.json")))
            : ((IReadOnlyList<TaskItem>)[], EmptyExtras<TaskId>());

        var (dayTemplates, dayTemplateExtras) = File.Exists(Path.Combine(dataDir, "day-templates.json"))
            ? DayTemplateCodec.Read(File.ReadAllText(Path.Combine(dataDir, "day-templates.json")))
            : ((IReadOnlyList<DayTemplate>)[], EmptyExtras<DayTemplateId>());

        PatternBook patterns;
        IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> patternExtras;
        IReadOnlyList<KeyValuePair<string, JsonElement>> patternEnvelopeExtras;
        var patternsPath = Path.Combine(dataDir, "patterns.json");
        if (File.Exists(patternsPath))
        {
            (patterns, patternExtras, patternEnvelopeExtras) = PatternCodec.Read(File.ReadAllText(patternsPath));
        }
        else
        {
            patterns = new PatternBook(default, []);
            patternExtras = EmptyExtras<PatternId>();
            patternEnvelopeExtras = [];
        }

        var (overrides, overrideExtras) = File.Exists(Path.Combine(dataDir, "overrides.json"))
            ? OverrideCodec.Read(File.ReadAllText(Path.Combine(dataDir, "overrides.json")))
            : ((IReadOnlyList<DateOverride>)[], EmptyExtras<DateOnly>());

        var (events, eventExtras) = File.Exists(Path.Combine(dataDir, "events.json"))
            ? EventCodec.Read(File.ReadAllText(Path.Combine(dataDir, "events.json")))
            : ((IReadOnlyList<Event>)[], EmptyExtras<EventId>());

        var eventExceptionsPath = Path.Combine(dataDir, "event-exceptions.json");
        IReadOnlyList<EventException> eventExceptions = File.Exists(eventExceptionsPath)
            ? EventCodec.ReadExceptions(File.ReadAllText(eventExceptionsPath))
            : [];

        var (completionLogs, completionExtras, derivedCompletions, derivedExtras) = LoadCompletions(dataDir);
        var (fires, fireExtras) = LoadFires(dataDir);

        return new StoreView(
            tasks, taskExtras,
            dayTemplates, dayTemplateExtras,
            patterns, patternExtras, patternEnvelopeExtras,
            overrides, overrideExtras,
            events, eventExtras,
            eventExceptions,
            completionLogs, completionExtras,
            derivedCompletions, derivedExtras,
            fires, fireExtras);
    }

    /// <summary>
    /// `completions/` holds one log per Task (filename is the TaskId, per
    /// <see cref="CompletionCodec.FileNameFor"/>) plus one envelope, `derived.json`, excluded
    /// from the per-Task scan by name.
    /// </summary>
    private static (
        IReadOnlyDictionary<TaskId, CompletionLog> Logs,
        IReadOnlyDictionary<TaskId, IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>>> Extras,
        IReadOnlyList<DerivedCompletionEntry> Derived,
        IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> DerivedExtras)
        LoadCompletions(string dataDir)
    {
        var completionsDir = Path.Combine(dataDir, "completions");

        var logs = new Dictionary<TaskId, CompletionLog>();
        var extras = new Dictionary<TaskId, IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>>>();
        IReadOnlyList<DerivedCompletionEntry> derived = [];
        IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> derivedExtras =
            EmptyExtras<DerivedCompletionKey>();

        if (Directory.Exists(completionsDir))
        {
            var derivedPath = Path.Combine(completionsDir, "derived.json");
            if (File.Exists(derivedPath))
            {
                (derived, derivedExtras) = CompletionCodec.ReadDerived(File.ReadAllText(derivedPath));
            }

            foreach (var path in Directory.EnumerateFiles(completionsDir, "*.json"))
            {
                if (Path.GetFileName(path) == "derived.json") continue;

                var taskId = new TaskId(Path.GetFileNameWithoutExtension(path));
                var (log, logExtras) = CompletionCodec.Read(taskId, File.ReadAllText(path));
                logs[taskId] = log;
                extras[taskId] = logExtras;
            }
        }

        return (logs, extras, derived, derivedExtras);
    }

    /// <summary>`fires/&lt;date&gt;.json` — one file per day; the date comes from the filename.</summary>
    private static (
        IReadOnlyDictionary<DateOnly, DayFires> Fires,
        IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>> Extras)
        LoadFires(string dataDir)
    {
        var firesDir = Path.Combine(dataDir, "fires");

        var fires = new Dictionary<DateOnly, DayFires>();
        var extras = new Dictionary<DateOnly, IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>>();

        if (Directory.Exists(firesDir))
        {
            foreach (var path in Directory.EnumerateFiles(firesDir))
            {
                var date = FireCodec.DateFromFileName(Path.GetFileName(path));
                if (date is null) continue;

                var (dayFires, fireExtras) = FireCodec.Read(date.Value, File.ReadAllText(path));
                fires[date.Value] = dayFires;
                extras[date.Value] = fireExtras;
            }
        }

        return (fires, extras);
    }

    private static IReadOnlyDictionary<TKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> EmptyExtras<TKey>()
        where TKey : notnull
        => new Dictionary<TKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>();

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
    /// Only a Tasks write is supported today: <paramref name="mutation"/> must return a
    /// <see cref="StoreMutation"/> whose single write is the new <see cref="TaskItem"/> list.
    /// </summary>
    public async Task MutateAsync(Func<IStoreView, StoreMutation> mutation, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var view = _current;
            var result = mutation(view);

            if (result.OrderedWrites is not [IReadOnlyList<TaskItem> callerTasks])
            {
                // A malformed StoreMutation is a caller/programming bug, not a disk failure —
                // deliberately not recorded as a write outcome either way.
                throw new NotImplementedException(
                    "JsonStore only writes tasks.json today — a StoreMutation must carry exactly one IReadOnlyList<TaskItem> write.");
            }

            // A defensive copy: the store must own its storage. `callerTasks` came straight out
            // of the caller's StoreMutation and IReadOnlyList<T> is not a promise of
            // immutability — a caller that keeps its own reference and mutates it later must
            // not be able to reach a view any concurrent reader already holds.
            IReadOnlyList<TaskItem> tasks = callerTasks.ToArray();

            var extras = view.TaskExtras;

            try
            {
                await WriteAtomicAsync(_tasksPath, writer => TaskCodec.Write(writer, tasks, extras), cancellationToken);
            }
            catch
            {
                Interlocked.Exchange(ref _lastWriteOutcome, WriteFailed);
                throw;
            }

            // Every other collection is carried forward unchanged from the pre-mutation view —
            // a Tasks-only write must not erase what StoreView.Load(dataDir) already put there.
            _current = new StoreView(
                tasks, extras,
                view.DayTemplates, view.DayTemplateExtras,
                view.Patterns, view.PatternExtras, view.PatternEnvelopeExtras,
                view.Overrides, view.OverrideExtras,
                view.Events, view.EventExtras,
                view.EventExceptions,
                view.CompletionLogs, view.CompletionExtras,
                view.DerivedCompletions, view.DerivedCompletionExtras,
                view.Fires, view.FireExtras);
            Interlocked.Exchange(ref _lastWriteOutcome, WriteSucceeded);
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
    IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> taskExtras,
    IReadOnlyList<DayTemplate> dayTemplates,
    IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> dayTemplateExtras,
    PatternBook patterns,
    IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> patternExtras,
    IReadOnlyList<KeyValuePair<string, JsonElement>> patternEnvelopeExtras,
    IReadOnlyList<DateOverride> overrides,
    IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> overrideExtras,
    IReadOnlyList<Event> events,
    IReadOnlyDictionary<EventId, IReadOnlyList<KeyValuePair<string, JsonElement>>> eventExtras,
    IReadOnlyList<EventException> eventExceptions,
    IReadOnlyDictionary<TaskId, CompletionLog> completionLogs,
    IReadOnlyDictionary<TaskId, IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>>> completionExtras,
    IReadOnlyList<DerivedCompletionEntry> derivedCompletions,
    IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> derivedCompletionExtras,
    IReadOnlyDictionary<DateOnly, DayFires> fires,
    IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>> fireExtras)
    : IStoreView
{
    public IReadOnlyList<TaskItem> Tasks { get; } = tasks;

    /// <summary>Unknown top-level properties per Task, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> TaskExtras { get; } = taskExtras;

    public IReadOnlyList<DayTemplate> DayTemplates { get; } = dayTemplates;

    /// <summary>Unknown top-level properties per Day template, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<DayTemplateId, IReadOnlyList<KeyValuePair<string, JsonElement>>> DayTemplateExtras { get; } = dayTemplateExtras;

    public PatternBook Patterns { get; } = patterns;

    /// <summary>Unknown top-level properties per Pattern, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<PatternId, IReadOnlyList<KeyValuePair<string, JsonElement>>> PatternExtras { get; } = patternExtras;

    /// <summary>Unknown properties on the `patterns.json` envelope itself — a channel of its own, not copied onto a Pattern.</summary>
    internal IReadOnlyList<KeyValuePair<string, JsonElement>> PatternEnvelopeExtras { get; } = patternEnvelopeExtras;

    public IReadOnlyList<DateOverride> Overrides { get; } = overrides;

    /// <summary>Unknown top-level properties per Override date, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<DateOnly, IReadOnlyList<KeyValuePair<string, JsonElement>>> OverrideExtras { get; } = overrideExtras;

    public IReadOnlyList<Event> Events { get; } = events;

    /// <summary>Unknown top-level properties per Event, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<EventId, IReadOnlyList<KeyValuePair<string, JsonElement>>> EventExtras { get; } = eventExtras;

    public IReadOnlyList<EventException> EventExceptions { get; } = eventExceptions;

    /// <summary>Every per-Task completion log that has a file, keyed by TaskId — the filename's inverse (<see cref="CompletionCodec.FileNameFor"/>).</summary>
    internal IReadOnlyDictionary<TaskId, CompletionLog> CompletionLogs { get; } = completionLogs;

    /// <summary>Unknown properties per completion entry, keyed by entry index, per Task.</summary>
    internal IReadOnlyDictionary<TaskId, IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, JsonElement>>>> CompletionExtras { get; } = completionExtras;

    public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions { get; } = derivedCompletions;

    /// <summary>Unknown properties per derived-completion entry, keyed on (ruleId, triggerId, due).</summary>
    internal IReadOnlyDictionary<DerivedCompletionKey, IReadOnlyList<KeyValuePair<string, JsonElement>>> DerivedCompletionExtras { get; } = derivedCompletionExtras;

    /// <summary>Every day's Fire record that has a file, keyed by date — the filename's inverse (<see cref="FireCodec.FileNameFor"/>).</summary>
    internal IReadOnlyDictionary<DateOnly, DayFires> Fires { get; } = fires;

    /// <summary>Unknown properties per Fire row, keyed on (windowId, kind), per date.</summary>
    internal IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<FireKey, IReadOnlyList<KeyValuePair<string, JsonElement>>>> FireExtras { get; } = fireExtras;

    /// <summary>An absent log reads as the empty log, never null and never a throw.</summary>
    public CompletionLog CompletionsFor(TaskId task) =>
        CompletionLogs.TryGetValue(task, out var log) ? log : CompletionLog.Empty(task);

    /// <summary>An absent day's Fire record reads as the empty record, never null and never a throw.</summary>
    public DayFires FiresOn(DateOnly date) =>
        Fires.TryGetValue(date, out var dayFires) ? dayFires : new DayFires(date, []);
}

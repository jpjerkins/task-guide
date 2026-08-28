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
        _current = Load(_tasksPath);
    }

    private static StoreView Load(string tasksPath)
    {
        if (!File.Exists(tasksPath))
        {
            return new StoreView([], new Dictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>>());
        }

        var (tasks, extras) = TaskCodec.Read(File.ReadAllText(tasksPath));
        return new StoreView(tasks, extras);
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

            _current = new StoreView(tasks, extras);
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
    IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> taskExtras)
    : IStoreView
{
    public IReadOnlyList<TaskItem> Tasks { get; } = tasks;

    /// <summary>Unknown top-level properties per Task, carried untouched across a load-and-save round trip.</summary>
    internal IReadOnlyDictionary<TaskId, IReadOnlyList<KeyValuePair<string, JsonElement>>> TaskExtras { get; } = taskExtras;

    public CompletionLog CompletionsFor(TaskId task) => throw new NotImplementedException();
    public IReadOnlyList<DerivedCompletionEntry> DerivedCompletions => throw new NotImplementedException();
    public IReadOnlyList<DayTemplate> DayTemplates => throw new NotImplementedException();
    public PatternBook Patterns => throw new NotImplementedException();
    public IReadOnlyList<DateOverride> Overrides => throw new NotImplementedException();
    public IReadOnlyList<Event> Events => throw new NotImplementedException();
    public IReadOnlyList<EventException> EventExceptions => throw new NotImplementedException();
    public DayFires FiresOn(DateOnly date) => throw new NotImplementedException();
}

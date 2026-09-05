using System.Text.Json;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;

namespace TaskGuide.Infrastructure.Storage;

/// <summary>
/// ADR-0009's apply phase: applies an already-valid <see cref="StartupPlan"/> and makes
/// <b>no conscious refusal of its own</b> — every decision was made by
/// <see cref="StartupPlanner.Plan"/>; only IO can stop this phase.
/// </summary>
public sealed class StartupWriter(IStore store, string dataDir, SnapshotWriter snapshotWriter, TimeProvider clock)
{
    /// <summary>
    /// Runs, in order: snapshot the plan's files (if any), run the plan's migration steps (if
    /// any), stamp `manifest.json` (if the plan names a version), then apply the plan's writes (if
    /// any). `manifest.json` is written only after every migration step succeeds, so a step that
    /// throws leaves the file at its pre-migration version and a retried startup attempts the
    /// whole walk again rather than resuming a partial one.
    /// </summary>
    public async Task ApplyAsync(StartupPlan plan, CancellationToken cancellationToken)
    {
        if (plan.FilesToSnapshot.Count > 0)
        {
            await snapshotWriter.TakeAsync(plan.FilesToSnapshot, clock.GetUtcNow(), cancellationToken);
        }

        foreach (var step in plan.MigrationSteps)
        {
            await step(dataDir, cancellationToken);
        }

        if (plan.ManifestVersion is { } version)
        {
            await WriteManifestAsync(version, cancellationToken);
        }

        // The callback discards the view MutateAsync hands it (`_ =>`), where the pre-split
        // StartupSequence.SweepRegistryAsync deliberately recomputed its plan *inside* that
        // callback and documented that as load-bearing. That is not a lost guard here: the
        // two-phase split means `plan` was computed in the plan phase by construction (ADR-0009 —
        // "a storage-owned writer applies that already-valid plan"), and startup is
        // single-threaded and runs before the host serves any request, so there is no concurrent
        // writer for a fresh view to reveal. This is a reachability argument, not a structural
        // one — a future caller invoking StartupWriter after the host is already serving would
        // break it.
        foreach (var mutation in plan.OrderedWrites)
        {
            await store.MutateAsync<Never>(_ => OneOf<StoreMutation, Never>.FromT0(mutation), cancellationToken);
        }
    }

    private async Task WriteManifestAsync(int version, CancellationToken cancellationToken)
    {
        var path = Path.Combine(dataDir, "manifest.json");
        var tempPath = Path.Combine(dataDir, $".manifest.json.tmp-{Guid.NewGuid():N}");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    ManifestCodec.Write(writer, version);
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

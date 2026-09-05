using OneOf;
using TaskGuide.Domain.Dimensions;

namespace TaskGuide.Application.Ports;

/// <summary>
/// ADR-0009's phase split, made structural: the plan phase sees the store only through
/// <see cref="IStoreReader"/> over a disposable bootstrap view, raises every conscious refusal,
/// and returns an immutable <see cref="StartupPlan"/> — it writes nothing, structurally, because
/// it is never handed anything that writes. A separate apply phase (not this interface) then
/// applies an already-valid plan, making no conscious refusal of its own; only IO may stop it
/// there. Only after that does a factory open the memory-authoritative runtime <c>IStore</c>.
/// </summary>
public interface IStartupPlanner
{
    OneOf<StartupPlan, StartupRefusal> Plan(IStoreReader bootstrap);
}

/// <summary>
/// Everything the write phase needs, and nothing it must decide — every decision already made,
/// so the apply phase can refuse nothing.
/// </summary>
/// <param name="FilesToSnapshot">
/// Empty means no snapshot: an empty store, or a startup that will migrate and sweep nothing, has
/// nothing for a snapshot to protect.
/// </param>
/// <param name="MigrationSteps">
/// A delegate list, not the <c>StoreMigration</c> step objects themselves — <c>StoreMigration</c>
/// is an Infrastructure type and <c>Application.Ports</c> cannot name it, and <c>StoreMigration.Apply</c>
/// is already exactly this delegate shape. The <em>selection</em> of which steps run happened at
/// plan time, in <see cref="IStartupPlanner.Plan"/>; running them here is IO, not a decision.
/// </param>
/// <param name="ManifestVersion">
/// Null means leave `manifest.json` alone (a store already at <c>CurrentVersion</c> with nothing
/// pending). Non-null is the version to stamp — either a completed migration walk's landing
/// version, or <c>CurrentVersion</c> itself for a fresh `/data` that has no manifest yet.
/// </param>
/// <param name="OrderedWrites">
/// Empty means make no <c>MutateAsync</c> call at all, not an empty one — <c>IStore.LastWriteSucceeded</c>
/// documents an actual disk write, and an empty mutation would wrongly report one.
/// </param>
public sealed record StartupPlan(
    IReadOnlyList<string> FilesToSnapshot,
    IReadOnlyList<Func<string, CancellationToken, Task>> MigrationSteps,
    int? ManifestVersion,
    IReadOnlyList<StoreMutation> OrderedWrites);

/// <summary>
/// The two conscious refusals a startup plan can raise, per #70 decision 3's rejection of shared
/// string-coded refusals — the union earns itself because only a registry collision signals
/// outbound before the process exits, and <c>Match</c> makes a third refusal kind break the
/// orchestrator at compile time rather than falling through unnoticed.
/// </summary>
[GenerateOneOf]
public partial class StartupRefusal : OneOfBase<RegistryCollision, StoreVersionAhead>;

/// <summary>
/// A Dimension registry collision (#21) — a duplicate value declared by two Dimensions. Carries the
/// facts, not a pre-formatted message, so <see cref="DuplicateDimensionValueException"/> can be
/// reconstructed byte-identical to the one <see cref="DimensionRegistry.AssertNoDuplicateValues"/>
/// itself throws, instead of nesting a re-formatted string inside its own formatter (#78).
/// </summary>
public sealed record RegistryCollision(string Value, IReadOnlyList<DimensionId> ClaimedBy);

/// <summary>`manifest.json`'s version (or a migration walk's landing version) is ahead of this
/// binary's `ManifestCodec.CurrentVersion` — a rollback must not silently down-migrate.</summary>
public sealed record StoreVersionAhead(int StoredVersion, int CurrentVersion);

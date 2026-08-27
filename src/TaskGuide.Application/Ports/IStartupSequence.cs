namespace TaskGuide.Application.Ports;

/// <summary>
/// <b>assert → snapshot → migrate → sweep → serve.</b> The one place a startup phase exists,
/// which is part of why memory-authoritative works at all.
/// </summary>
public interface IStartupSequence
{
    /// <summary>
    /// The Dimension registry assert (#21). A duplicate value refuses to run — a crash loop that
    /// pushes nothing — so it signals failure outbound, carrying its reason, before exiting.
    /// </summary>
    void AssertRegistry();

    /// <summary>
    /// <c>/data/snapshots/&lt;utc&gt;/</c>, whole-file copies of everything the sweep is about to
    /// modify, written <b>once per startup and only when that startup will actually write</b>,
    /// keeping the last <b>5</b>. Five rather than one because the realistic failure is a bad
    /// rename shipping and the service restarting twice more before anyone notices.
    /// <para>A Snapshot sits <em>on</em> the protected volume and guards against this service's
    /// own writes; a <b>Backup</b> sits off it and guards against losing the volume. Never one word.</para>
    /// </summary>
    Task SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ordered N→N+1 steps against the store-wide version in <c>manifest.json</c>. Per-file
    /// versions were rejected — files could drift to different versions. Most changes need no
    /// migration at all: additive fields take defaults on read, and <b>unknown fields are
    /// preserved, not dropped</b>, which is what keeps a rollback lossless.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The promote/demote sweep: a loose Tag the registry now claims moves into its Dimension's
    /// slot, and a withdrawn value's Tags go loose again with their strings intact. An ordinal
    /// axis takes up a loose Tag <b>only if that record has no value on it</b> — a value chosen
    /// deliberately is never overruled by one that was loose.
    /// </summary>
    Task SweepRegistryAsync(CancellationToken cancellationToken);
}

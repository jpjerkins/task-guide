using OneOf;
using TaskGuide.Domain.Time;

namespace TaskGuide.Domain.Firing;

/// <summary>
/// The closed set of planned fire shapes. Window data belongs only to the arms that can name a
/// Window, so a fallback carrying one cannot be constructed.
/// </summary>
[GenerateOneOf]
public partial class FireIntentKind : OneOfBase<WindowFire, InWindowSnooze, PastWindowSnooze, UnconditionalFire, FallbackFire>;

public sealed record WindowFire(ResolvedWindow Window);

public sealed record InWindowSnooze(ResolvedWindow Window);

public sealed record PastWindowSnooze;

public sealed record UnconditionalFire;

public sealed record FallbackFire;

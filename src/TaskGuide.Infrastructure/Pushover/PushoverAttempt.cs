// PushoverAttempt: the outcome of one HTTP attempt to reach Pushover, and the only question a
// retry loop needs answered — a failed Receipt is indistinguishable from an ignored one (no tap
// event below emergency priority, #3), so the boundary the retry logic draws is *acceptance*.
// See PushoverAccepted/PushoverRefused/PushoverRejected below for what each arm means for the
// loop. A plain OneOf alias, not [GenerateOneOf] — the source generator's package isn't
// referenced by this project; Storage/JsonStore.cs's `OneOf<Applied, T>` is the same idiom
// already in use here. PushoverOptions.IsConfigured being false is not a fourth arm: it isn't an
// attempt at all, and is handled as an early return before the retry loop, not a Match case.
global using PushoverAttempt = OneOf.OneOf<
    TaskGuide.Infrastructure.Pushover.PushoverAccepted,
    TaskGuide.Infrastructure.Pushover.PushoverRefused,
    TaskGuide.Infrastructure.Pushover.PushoverRejected>;

namespace TaskGuide.Infrastructure.Pushover;

/// <summary>2xx: Pushover has the message. Stop the retry loop; the send succeeded.</summary>
public sealed record PushoverAccepted;

/// <summary>
/// A refused connection, a timeout, or a 5xx: Pushover never accepted, so a retry still can't
/// double-notify anything already delivered. Retry. <see cref="Reason"/> is a log-only detail,
/// not part of the arm's meaning.
/// </summary>
public sealed record PushoverRefused(string Reason);

/// <summary>
/// A 4xx: a bad token or a malformed message fails the same way three times. Stop the retry loop;
/// the send failed for good. <see cref="StatusCode"/> is a log-only detail.
/// </summary>
public sealed record PushoverRejected(int StatusCode);

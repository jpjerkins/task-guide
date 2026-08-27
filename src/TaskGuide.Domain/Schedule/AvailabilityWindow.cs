using TaskGuide.Domain.Common;
using TaskGuide.Domain.Tags;

namespace TaskGuide.Domain.Schedule;

/// <summary>
/// A clock-bounded span within a day, carrying a name and a set of Dimension values. A Window
/// firing <em>is</em> the reminder, and its Dimension values are the filter selecting matching
/// Tasks — there is no separate reminder-definition concept.
/// </summary>
/// <remarks>
/// A Window is a <b>per-day instance</b>, not a shared definition: "Evening" on Tuesday and
/// "Evening" on Saturday are two Windows that happen to share a label. Start and End are
/// <b>authored</b>, so they are clock times resolved per date — never frozen to an instant.
/// The Name is reminder copy, not an identity.
/// </remarks>
public sealed record AvailabilityWindow(
    WindowId Id,
    string Name,
    TimeOnly Start,
    TimeOnly End,
    TagSet Tags);

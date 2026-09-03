using TaskGuide.Domain.Schedule;

namespace TaskGuide.Domain.Time;

/// <summary>
/// An Availability Window's Start and End, resolved to instants on a given date. Deliberately a
/// plain nullable at the call site rather than a union: ADR-0011 keeps nullable as the idiom for
/// plain absence, and a union is for a choice <em>between</em> shapes — there is only one shape
/// here, present or not.
/// </summary>
public sealed record ResolvedWindow(AvailabilityWindow Window, DateTimeOffset Start, DateTimeOffset End);

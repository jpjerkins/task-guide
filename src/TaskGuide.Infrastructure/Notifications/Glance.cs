namespace TaskGuide.Infrastructure.Notifications;

/// <summary>
/// Three text lines, ~20 characters each in practice — a watchOS complication's budget. Inside a
/// Window with ≥1 match: rank 1, rank 2, "+N more doable now" (required, not decorative — the
/// matching-now total appears nowhere else on the face). Otherwise: the next Window's start, its
/// rank 1, its rank 2 — the fall-through that keeps the Glance from ever being blank, since a
/// dead-looking complication cannot be told apart from a broken one. No Durations, no
/// fetched-failure note.
/// </summary>
/// <remarks>
/// The presentation type — nothing renders into it yet. <see cref="TaskGuide.Domain.Notifications.GlanceState"/>
/// is the domain shape this is rendered from; the renderer belongs to the Adapters lane and does
/// not exist yet.
/// </remarks>
public sealed record Glance(int Count, string Line1, string Line2, string Line3);

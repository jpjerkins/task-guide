using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Firing;
using TaskGuide.Domain.Time;

namespace TaskGuide.Application.Reminders;

public sealed class SnoozeWindow(IStore store, TimeProvider timeProvider, DayBoundary boundary)
{
    public async Task<SnoozeOutcome> ExecuteAsync(DateOnly date, WindowId windowId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var result = await store.MutateAsync<SnoozeOutcome>(view =>
        {
            var reminder = view.FiresOn(date).Rows.SingleOrDefault(row =>
                row.WindowId is not null
                && row.WindowId.Equals(windowId)
                && row.Kind is (FireKind.Window or FireKind.Unconditional)
                && row.IsFired);
            if (reminder?.WindowStart is not { } start || reminder.WindowEnd is not { } end)
                return OneOf<StoreMutation, SnoozeOutcome>.FromT1((SnoozeOutcome)new ReminderNotFound());
            var interval = SnoozePolicy.IntervalFor(end.ToTimeSpan() - start.ToTimeSpan());
            var dayBoundary = boundary.EndOf(date);
            if (ReminderSuppression.SnoozeLine(now, interval, dayBoundary) is { } suppression)
                return OneOf<StoreMutation, SnoozeOutcome>.FromT1((SnoozeOutcome)new SnoozeUnavailable(suppression));
            var row = reminder with
            {
                Kind = FireKind.Snooze,
                DueAt = now + interval,
                FiredAt = null,
                Matched = null,
                Carried = null,
            };
            var rows = view.FiresOn(date).Rows
                .Where(candidate => candidate.WindowId is null
                    || !candidate.WindowId.Equals(windowId)
                    || candidate.Kind != FireKind.Snooze)
                .Append(row)
                .ToArray();
            return OneOf<StoreMutation, SnoozeOutcome>.FromT0(
                new StoreMutation([new FiresWrite(new DayFires(date, rows))]));
        }, cancellationToken);
        return result.Match<SnoozeOutcome>(_ => new Snoozed(), refusal => refusal);
    }
}

public sealed record Snoozed;
public sealed record SnoozeUnavailable(string Line);
public sealed record ReminderNotFound;

[GenerateOneOf]
public partial class SnoozeOutcome : OneOfBase<Snoozed, SnoozeUnavailable, ReminderNotFound>;

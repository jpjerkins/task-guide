namespace TaskGuide.Application.Ports;

/// <summary>Runs the unguarded fire-record retention sweep for the current local date.</summary>
public interface IFireRetention
{
    FireSweepResult Sweep(DateOnly today);
}

/// <summary>Dates a retention sweep removed and dates it could not remove.</summary>
public sealed record FireSweepResult(IReadOnlyList<DateOnly> Removed, IReadOnlyList<DateOnly> Failed);

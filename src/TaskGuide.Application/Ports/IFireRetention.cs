namespace TaskGuide.Application.Ports;

/// <summary>Runs the unguarded fire-record retention sweep for the current local date.</summary>
public interface IFireRetention
{
    void Sweep(DateOnly today);
}

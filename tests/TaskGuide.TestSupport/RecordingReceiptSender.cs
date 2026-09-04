using TaskGuide.Application.Ports;
using TaskGuide.Domain.Notifications;

namespace TaskGuide.TestSupport;

/// <summary>
/// Records every <see cref="Receipt"/> it was handed and reports a configurable outcome,
/// defaulting to success. Never throws — <see cref="IReceiptSender"/>'s doc is explicit that
/// adapters never throw for expected failures, they return an outcome; a fake that threw would
/// train callers to wrap it in a <c>try</c>, the opposite of the contract.
/// </summary>
public sealed class RecordingReceiptSender : IReceiptSender
{
    public List<Receipt> Receipts { get; } = [];
    private bool _failNext;

    /// <summary>Makes the next <see cref="SendReceiptAsync"/> call return <c>false</c>.</summary>
    public void FailNextSend() => _failNext = true;

    public Task<bool> SendReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        Receipts.Add(receipt);
        var succeeded = !_failNext;
        _failNext = false;
        return Task.FromResult(succeeded);
    }
}

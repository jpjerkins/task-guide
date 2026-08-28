using System.Security.Cryptography;
using TaskGuide.Domain.Common;

namespace TaskGuide.Infrastructure.Ids;

/// <summary>
/// Mints type-prefixed ULIDs (#23): a 48-bit millisecond timestamp plus 80 bits of randomness,
/// Crockford Base32-encoded to 26 characters, matching the fixture format exactly
/// (<c>t_01ARZ3NDEKTSV4RRFFQ69G5FAV</c>).
/// </summary>
public sealed class UlidIdMinter : IIdMinter
{
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public TaskId NextTaskId() => new(TaskId.Prefix + NewUlid());

    public WindowId NextWindowId() => new(WindowId.Prefix + NewUlid());
    public DayTemplateId NextDayTemplateId() => new(DayTemplateId.Prefix + NewUlid());
    public PatternId NextPatternId() => new(PatternId.Prefix + NewUlid());
    public EventId NextEventId() => new(EventId.Prefix + NewUlid());
    public EventPrototypeId NextEventPrototypeId() => new(EventPrototypeId.Prefix + NewUlid());

    /// <summary>
    /// 26 Crockford Base32 characters: 10 for the 48-bit timestamp (an implicit 50-bit field —
    /// the top 2 bits are always 0, which a plain right-shift on a 64-bit value already gives
    /// for free) plus 16 for the 80-bit randomness (already an exact multiple of 5, no padding).
    /// </summary>
    internal static string NewUlid()
    {
        var timestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Span<byte> randomBytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomBytes);
        UInt128 randomness = 0;
        foreach (var b in randomBytes) randomness = (randomness << 8) | b;

        Span<char> chars = stackalloc char[26];

        for (var i = 0; i < 10; i++)
        {
            var shift = 45 - i * 5;
            chars[i] = CrockfordBase32[(int)((timestampMs >> shift) & 0x1F)];
        }

        for (var i = 0; i < 16; i++)
        {
            var shift = 75 - i * 5;
            chars[10 + i] = CrockfordBase32[(int)((randomness >> shift) & 0x1F)];
        }

        return new string(chars);
    }
}

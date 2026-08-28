namespace TaskGuide.Infrastructure.Pushover;

/// <summary>
/// The two static secrets (#3): an application API token and a user key. Bound from
/// configuration/environment (<c>Pushover:Token</c>, <c>Pushover:UserKey</c>) — never hardcoded,
/// never committed.
/// </summary>
public sealed class PushoverOptions
{
    public const string SectionName = "Pushover";

    public string? Token { get; set; }
    public string? UserKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(UserKey);
}

using OneOf;

namespace TaskGuide.Domain.Common;

/// <summary>
/// The outcome of a fetched Dimension source (#69): a live value, or an outage — never a nullable
/// list standing in for both. #68 makes the unknown/empty distinction load-bearing in <b>opposite</b>
/// directions — matching fails closed on <see cref="Unavailable"/>, counting fails loud — and a
/// single <c>?? []</c> would collapse that silently.
/// </summary>
[GenerateOneOf]
public partial class FetchOutcome<T> : OneOfBase<Known<T>, Unavailable>;

public sealed record Known<T>(T Value);

public sealed record Unavailable(string Reason);

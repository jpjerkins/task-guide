namespace TaskGuide.Api.Endpoints;

/// <summary>
/// All three iOS Shortcuts write the <b>same</b> Task through this one endpoint, which carries a
/// <b>source</b>. The Receipt policy reads that source — keeping the decision in one place rather
/// than letting each capture path decide.
/// </summary>
/// <remarks>
/// Duration is the only property capture must supply; a capture that drops it produces an
/// `Unprocessed` Task, which <em>is</em> that label's definition. No capture path collects Tags —
/// they are added afterwards through the Receipt. <b>Capture is never queued</b>: one attempt
/// against a reachable server, failing loudly at the moment of capture.
/// </remarks>
public static class CaptureEndpoints
{
    public static RouteGroupBuilder MapCaptureEndpoints(this RouteGroupBuilder api)
    {
        var capture = api.MapGroup("/capture").WithTags("Capture");

        // { title, duration?, energy?, deadline?, source }. The server snaps raw minutes UP to a
        // bucket, so a capture path may send "45".
        capture.MapPost("/", () => Results.NoContent());

        return capture;
    }
}

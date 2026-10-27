using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The developer allowed reading the page (once or always): read it now; its text is the result of the paused call.
    public async Task<string> RunApprovedFetchAsync(PendingFetch fetch, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        await AnnounceAsync(PlannerTools.WebFetch, new JsonObject { ["url"] = secrets.Redact(fetch.Url) }, "Planner", string.Empty, recorder, progress, cancellationToken);
        var maxChars = Math.Clamp(fetch.MaxChars ?? MaxFetchChars, 1_000, MaxFetchChars);
        return secrets.Redact(await FetchAsync(new Uri(fetch.Url), maxChars, input, string.Empty, recorder, progress, cancellationToken));
    }
}

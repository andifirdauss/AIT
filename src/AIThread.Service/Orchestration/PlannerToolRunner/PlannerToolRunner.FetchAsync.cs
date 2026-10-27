using System.Text;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Testing;
using AIThread.Service.Workspace.Web;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The first URL was checked by the caller (local, granted or allowed just now). A redirect stays allowed on the same
    // host (http → https, a trailing slash) and on this machine; another host needs its own Host grant.
    private async Task<string> FetchAsync(Uri url, int maxChars, PlannerInput input, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var result = await webFetcher.FetchAsync(url, async (next, token) =>
            string.Equals(next.IdnHost, url.IdnHost, StringComparison.OrdinalIgnoreCase)
            || TestUrls.IsLocal(next.ToString())
            || await approvals.UseAsync(input.Project.ProjectId, ApprovalKind.Host, WebUrls.HostTarget(next), recorder.Thread.Id, token), cancellationToken);

        var seconds = $"{result.DurationMs / 1000.0:0.0}s";
        var headline = result.Error is { } failed ? $"❌ {failed}"
            : result.BlockedRedirect is { } blocked ? Lang.T($"🔒 dialihkan ke host lain {blocked.IdnHost}", $"🔒 redirected to another host {blocked.IdnHost}")
            : $"{(result.StatusCode is >= 200 and < 300 ? "✅" : "⚠️")} {result.StatusCode} · {result.ContentType ?? "?"} · {Size(result.Bytes)} · {seconds}";
        recorder.Log(result.Error is null && result.StatusCode is >= 200 and < 400 ? ActivityLevel.Info : ActivityLevel.Warning,
            $"Planner → web_fetch {secrets.Redact(url.ToString())}: {headline}");
        await progress.DetailAsync(headline, cancellationToken, hang: Hang(prefix));

        if (result.BlockedRedirect is { } redirect)
            return $"The page redirects to {secrets.Redact(redirect.ToString())}, on another host that needs the developer's permission. Call web_fetch with that URL to read it.";
        if (result.Error is not null && result.StatusCode is null) return $"Error: {result.Error}";

        var builder = new StringBuilder($"GET {result.FinalUrl} → {result.StatusCode} {result.ReasonPhrase} · {result.ContentType ?? "no content type"} · {Size(result.Bytes)}");
        if (result.BodyCut) builder.Append($" (only the first {Size(WebFetcher.MaxBodyBytes)} read)");
        builder.Append('\n');
        if (result.FinalUrl != result.RequestedUrl) builder.Append($"(redirected from {result.RequestedUrl})\n");
        if (result.Title is { } title) builder.Append($"Title: {title}\n");
        builder.Append('\n');
        if (result.Error is not null) return builder.Append($"Error: {result.Error}").ToString();
        if (result.Text.Length == 0) return builder.Append("(empty body)").ToString();

        if (result.Text.Length <= maxChars) return builder.Append(result.Text).ToString();
        return builder.Append(result.Text[..maxChars])
            .Append($"\n... (cut at {maxChars} of {result.Text.Length} characters; fetch a more specific page, e.g. one section of the docs)").ToString();
    }
}

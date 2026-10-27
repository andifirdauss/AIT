using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Testing;
using AIThread.Service.Workspace.Web;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The app on this machine is read at once; another host needs a Host grant (the same one a curl to it uses),
    // otherwise the planner pauses with an approval request (the page is read when the developer allows it,
    // RunApprovedFetchAsync). A tools node cannot pause: it is told to leave the URL to the planner.
    private async Task<PlannerToolOutcome> WebFetchAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var hang = Hang(prefix);
        if (!input.Settings.WebFetchEnabled)
        {
            await progress.DetailAsync(Lang.T("🟡 web_fetch dimatikan di pengaturan", "🟡 web_fetch turned off in the settings"), cancellationToken, hang: hang);
            return new PlannerToolOutcome("Error: reading web pages is disabled in the AIT settings.", null, true);
        }
        if (!WebUrls.TryParse(Str(args, "url"), out var url, out var error)) return new PlannerToolOutcome($"Error: {error}", null, true);
        if (WebUrls.IsRefused(url))
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Planner → web_fetch ditolak: {url.IdnHost} alamat metadata cloud", $"Planner → web_fetch refused: {url.IdnHost} is a cloud metadata address"));
            await progress.DetailAsync(Lang.T("⛔ alamat metadata cloud tidak pernah dibaca", "⛔ a cloud metadata address is never read"), cancellationToken, hang: hang);
            return new PlannerToolOutcome($"Error: {url.IdnHost} is a cloud metadata address, which is never fetched.", null, true);
        }

        var maxChars = Math.Clamp(Int(args, "max_chars") ?? MaxFetchChars, 1_000, MaxFetchChars);
        if (!TestUrls.IsLocal(url.ToString()))
        {
            var item = new ApprovalItem(ApprovalKind.Host, WebUrls.HostTarget(url), $"GET {secrets.Redact(url.GetLeftPart(UriPartial.Path))}");
            if (!await AllGrantedAsync(input, [item], recorder, cancellationToken))
            {
                if (!input.CanApprove)
                {
                    await progress.DetailAsync(Lang.T("🔒 butuh izin developer, tidak bisa ditanyakan di sini", "🔒 needs the developer's permission, which cannot be asked here"), cancellationToken, hang: hang);
                    return new PlannerToolOutcome(prefix.Length > 0
                        ? $"Error: reading {item.Target} needs the developer's permission, which a tools node cannot ask for. Report this URL in your findings so the planner can fetch it itself."
                        : $"Error: reading {item.Target} needs the developer's permission, and they cannot be asked from here. Do without it, or name the page in the summary for the developer to check.", null, true);
                }

                var reason = Str(args, "reason");
                var text = Lang.T("Planner ingin membaca halaman web", "The planner wants to read a web page") + (reason is null ? "." : $": {reason}") +
                           "\n\n" + Lang.T($"butuh izin karena: host luar {item.Target}", $"needs permission because: outside host {item.Target}");
                await progress.DetailAsync(Lang.T("🔐 menunggu izin developer", "🔐 waiting for the developer's permission"), cancellationToken, hang: hang);
                recorder.Log(ActivityLevel.Info, Lang.T($"Planner minta izin membaca: {item.Target}", $"The planner asks permission to read: {item.Target}"));
                var approval = new PlannerApproval(new ApprovalRequest([item], text), Fetch: new PendingFetch(url.ToString(), reason, maxChars));
                return new PlannerToolOutcome(string.Empty, null, Question: PlannerQuestion.ForApproval(approval));
            }
        }

        return new PlannerToolOutcome(await FetchAsync(url, maxChars, input, prefix, recorder, progress, cancellationToken), null);
    }
}

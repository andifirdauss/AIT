using AIThread.Common.Enums;
using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private const string DetectedBy = "AIT Tester";

    // The first test of a project with several accounts looks for "login as" once (a scan of the code, then a cheap
    // model); later runs read the saved result. A failed detection only costs the shortcut: switch_user logs in normally.
    private async Task EnsureImpersonationAsync(TestRun run, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (run.Accounts is not { Accounts.Count: > 1 } set || set.Settings.Impersonation is not null) return;
        try
        {
            await progress.StepAsync(Lang.T("🔎 Mencari fitur login as di kode project (sekali per project)", "🔎 Searching the project code for a login-as feature (once per project)"), cancellationToken, hang: 3);
            var info = await impersonation.DetectAsync(run.Input.Project, DetectedBy, cancellationToken);
            run.Accounts = await accountStore.GetAsync(run.Input.Project.Key, cancellationToken);
            recorder.Log(ActivityLevel.Info, Lang.T($"Deteksi login as: {info?.Status ?? "unknown"}", $"Login as detection: {info?.Status ?? "unknown"}") + (info?.IsAvailable == true ? $" — {info.How}" : "."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Login-as detection of {Project} failed; the tester logs in normally.", run.Input.Project.Name);
        }
    }
}

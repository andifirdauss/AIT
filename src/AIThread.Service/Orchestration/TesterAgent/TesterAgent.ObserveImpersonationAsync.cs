using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Memory;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // A "login as" the tester saw in the app (report_test "impersonation") joins the project's settings, unconfirmed,
    // unless a developer or a working detection already says how.
    private async Task ObserveImpersonationAsync(string arguments, TestRun run, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(run.Input.Project.Key)) return;
        if (PlannerToolRunner.ParseArguments(arguments, out _)?["impersonation"] is not JsonObject seen) return;
        var how = PlannerToolRunner.Str(seen, "how")?.Trim();
        if (string.IsNullOrEmpty(how)) return;

        var roles = MemoryAi.Strings(seen["roles"]).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var back = PlannerToolRunner.Str(seen, "back")?.Trim();
        var info = new ImpersonationInfo(ImpersonationInfo.Available, how, string.IsNullOrEmpty(back) ? null : back, roles, [], ImpersonationInfo.FromTester, false,
            DateTime.UtcNow, DetectedBy);
        try
        {
            if (await impersonation.ObserveAsync(run.Input.Project.Key, info, DetectedBy, cancellationToken))
                recorder.Log(ActivityLevel.Info, $"Tester melihat login as di aplikasi; dicatat di Akun uji (belum dikonfirmasi): {how}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The tester's login-as observation could not be saved.");
        }
    }
}

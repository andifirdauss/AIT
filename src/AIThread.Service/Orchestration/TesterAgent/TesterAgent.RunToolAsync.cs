using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private async Task<string> RunToolAsync(ToolCall call, TestRun run, ThreadRecorder recorder, ClientProgress progress, string? badge, CancellationToken cancellationToken)
    {
        var available = TesterTools.Available(run.Test.Level, canSwitchUser: CanSwitchUser(run));
        if (!available.Contains(call.Name))
            return $"Error: tool '{call.Name}' is not available. Available: {string.Join(", ", available)}, {TesterTools.ReportTest}.";

        var args = PlannerToolRunner.ParseArguments(call.Arguments, out var error);
        if (args is null) return error!.Output;

        // The progress and the log show the placeholders; only the tool gets the real values.
        var detail = Describe(call.Name, args, run.Test.BaseUrl!);
        if (run.Login is { } login) args = (JsonObject)Fill(args, login)!;
        // Remembered values are not secret; the progress keeps showing their placeholders like the model wrote them.
        if (run.Report.Values.Count > 0 && call.Name != TesterTools.Remember) args = (JsonObject)FillValues(args, run.Report.Values)!;
        // A token the app returned earlier reached the model as a placeholder; the request gets the real one.
        secrets.RestoreJson(args);
        recorder.Log(ActivityLevel.Debug, $"Tester → {call.Name} {detail}");
        await progress.StepAsync(PlannerToolRunner.Lead(string.Empty, call.Name) + ClientProgress.BadgeSlot(badge) + detail, cancellationToken,
            hang: PlannerToolRunner.LeadColumn + ClientProgress.SlotWidth);

        try
        {
            return run.Mask(call.Name switch
            {
                TesterTools.HttpRequest => await HttpRequestAsync(args, run, recorder, progress, cancellationToken),
                TesterTools.SwitchUser => await SwitchUserAsync(args, run, recorder),
                TesterTools.Remember => Remember(args, run, recorder),
                _ => await BrowserAsync(call.Name, args, run, progress, cancellationToken)
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = run.Mask(ex.Message);
            recorder.Log(ActivityLevel.Warning, $"Tester: {call.Name} gagal: {message}");
            return $"Error: {message}";
        }
    }
}

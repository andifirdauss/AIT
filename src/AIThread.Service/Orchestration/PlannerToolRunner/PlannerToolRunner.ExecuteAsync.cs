using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private async Task<string> ExecuteAsync(string command, string workingDirectory, PlannerInput input, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(command, workingDirectory, TimeSpan.FromSeconds(input.Settings.CommandTimeoutSeconds), cancellationToken);
        var seconds = $"{result.DurationMs / 1000.0:0.0}s";
        var headline = result.StartError is { } startError ? $"❌ {startError}"
            : result.TimedOut ? Lang.T($"⏱️ dihentikan setelah {input.Settings.CommandTimeoutSeconds} detik", $"⏱️ stopped after {input.Settings.CommandTimeoutSeconds} seconds")
            : $"{(result.ExitCode == 0 ? "✅" : "⚠️")} exit {result.ExitCode} · {seconds}";
        recorder.Log(result.Succeeded ? ActivityLevel.Info : ActivityLevel.Warning, $"Planner → run_command `{secrets.Redact(command)}`: {headline}");
        await progress.DetailAsync(headline, cancellationToken, hang: Hang(prefix));

        if (result.StartError is not null) return $"Error: {result.StartError}";
        var status = result.TimedOut
            ? $"Stopped after {input.Settings.CommandTimeoutSeconds} seconds (timeout); the output so far:"
            : $"Exit code {result.ExitCode} after {seconds} ({result.Shell}, in {input.Scope.ToDisplay(workingDirectory)}).";
        // Masked as a config file too: a command may print .env or appsettings lines ("DB_PASSWORD=..."), which the
        // plain rules leave alone outside such a file.
        var output = result.Output.Length == 0 ? "(no output)" : secrets.RedactFile(".env", result.Output, input.Scope.Secrets.Keys);
        return $"{status}\n{output}" + (result.Truncated ? $"\n... (output cut at {CommandRunner.MaxOutputChars} characters; filter it, e.g. with jq or head)" : string.Empty);
    }
}

using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Starts the (allowed) background process, waits as asked, and tells the planner its id, state and output so far.
    private async Task<string> LaunchAsync(string command, string workingDirectory, BackgroundWait wait, PlannerInput input, string prefix, ThreadRecorder recorder,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        var started = await processes.StartAsync(recorder.Thread.Id, command, workingDirectory, wait, cancellationToken);
        var masked = secrets.Redact(command);
        if (started.Id is not { } id)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Planner → start_process `{masked}` gagal: {started.Error}", $"Planner → start_process `{masked}` failed: {started.Error}"));
            await progress.DetailAsync($"❌ {started.Error}", cancellationToken, hang: Hang(prefix));
            return $"Error: {started.Error}.";
        }

        var seconds = $"{started.WaitedMs / 1000.0:0.0}s";
        var headline = started.Exited ? $"⚠️ {id} exit {started.ExitCode?.ToString() ?? "?"} · {seconds}"
            : started.Ready ? $"✅ {id} " + Lang.T("berjalan", "running") + $" · {seconds}"
            : $"🟡 {id} " + Lang.T("berjalan, belum siap", "running, not ready yet") + $" · {seconds}";
        recorder.Log(started.Exited ? ActivityLevel.Warning : ActivityLevel.Info,
            Lang.T($"Planner → start_process `{masked}` di latar belakang: {headline}", $"Planner → start_process `{masked}` in the background: {headline}"));
        await progress.DetailAsync(headline, cancellationToken, hang: Hang(prefix));

        var place = $"({started.Shell}, in {input.Scope.ToDisplay(workingDirectory)})";
        var awaited = wait.Url is not null ? $"{wait.Url} answering" : !string.IsNullOrEmpty(wait.Text) ? $"the text \"{wait.Text}\"" : null;
        var status = started.Exited
            ? $"Process {id} exited with code {started.ExitCode?.ToString() ?? "?"} after {seconds} {place}; it is not running. Its output:"
            : started.Ready && awaited is not null
                ? $"Process {id} is running {place}; waited {seconds} for {awaited}. Stop it with stop_process when you are done. Output so far:"
                : started.Ready
                    ? $"Process {id} is running {place} after {seconds}. Stop it with stop_process when you are done. Output so far:"
                    : $"Process {id} is running {place}, but there was no {awaited} within {wait.Seconds} seconds. Check it with process_output, or stop it with stop_process. Output so far:";
        var output = started.Output.Trim();
        var cut = output.Length > CommandRunner.MaxOutputChars;
        if (cut) output = output[^CommandRunner.MaxOutputChars..];
        output = output.Length == 0 ? "(no output)" : secrets.RedactFile(".env", output, input.Scope.Secrets.Keys);
        return $"{status}\n{output}" + (cut ? $"\n... (only the last {CommandRunner.MaxOutputChars} characters)" : string.Empty);
    }
}

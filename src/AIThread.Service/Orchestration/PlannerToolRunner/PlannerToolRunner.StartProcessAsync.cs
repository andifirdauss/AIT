using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Testing;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // A background process always needs permission (it keeps running, and may change things while it does): it starts
    // when a saved grant covers its command, otherwise the planner pauses with an approval request and it starts once
    // the developer allows it (RunApprovedCommandAsync). Credential files are refused as in run_command.
    private async Task<PlannerToolOutcome> StartProcessAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var hang = Hang(prefix);
        if (!input.Settings.CommandToolEnabled)
        {
            await progress.DetailAsync(Lang.T("🟡 perintah dimatikan di pengaturan", "🟡 commands turned off in the settings"), cancellationToken, hang: hang);
            return new PlannerToolOutcome("Error: running commands is disabled in the AIT settings.", null, true);
        }

        var command = Str(args, "command");
        if (command is null) return new PlannerToolOutcome("Error: \"command\" is required.", null, true);
        var workingDirectory = input.Scope.Root;
        if (Str(args, "cwd") is { } cwd)
        {
            if (!input.Scope.TryResolveForRead(cwd, out var full, out var error)) return new PlannerToolOutcome($"Error: {error}", null, true);
            if (!Directory.Exists(full)) return new PlannerToolOutcome($"Error: the folder '{cwd}' does not exist.", null, true);
            workingDirectory = full;
        }

        var url = Str(args, "wait_for_url");
        if (url is not null && !TestUrls.IsLocal(url))
            return new PlannerToolOutcome($"Error: wait_for_url must be an http(s) URL on localhost, not '{url}'.", null, true);
        var text = Str(args, "wait_for_text");
        var seconds = Int(args, "wait_seconds") ?? (url is not null || text is not null ? 60 : 5);
        var wait = new BackgroundWait(url, text, Math.Clamp(seconds, 1, BackgroundProcesses.MaxWaitSeconds));
        if (processes.Running(recorder.Thread.Id) >= BackgroundProcesses.MaxPerThread)
            return new PlannerToolOutcome($"Error: this thread already runs {BackgroundProcesses.MaxPerThread} background processes; stop one with stop_process first.", null, true);

        var check = CommandPolicy.Classify(command, input.Scope, workingDirectory);
        if (check.Verdict == CommandVerdict.Refused)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Planner → start_process ditolak: {check.Reason}", $"Planner → start_process refused: {check.Reason}"));
            await progress.DetailAsync($"⛔ {check.Reason}", cancellationToken, hang: hang);
            return new PlannerToolOutcome($"Error: the command was refused: {check.Reason}.", null, true);
        }

        // The developer sees (and a grant keeps) the command with its secrets masked.
        var items = new List<ApprovalItem> { new(ApprovalKind.Command, secrets.Redact(command), Lang.T("berjalan di latar belakang", "runs in the background")) };
        items.AddRange((check.Items ?? []).Where(i => i.Kind != ApprovalKind.Command));
        if (!await AllGrantedAsync(input, items, recorder, cancellationToken))
        {
            if (!input.CanApprove)
            {
                await progress.DetailAsync(Lang.T("🔒 butuh izin developer, tidak bisa ditanyakan di sini", "🔒 needs the developer's permission, which cannot be asked here"), cancellationToken, hang: hang);
                return new PlannerToolOutcome("Error: a background process needs the developer's permission, and they cannot be asked from here. Do without it, or tell them in the summary what to start themselves.", null, true);
            }

            var reason = Str(args, "reason");
            var message = Lang.T("Planner ingin menjalankan proses di latar belakang", "The planner wants to start a background process") + (reason is null ? "." : $": {reason}") +
                          $"\n\n`{input.Scope.ToDisplay(workingDirectory)}` · " +
                          Lang.T($"AIT menghentikannya saat thread selesai (paling lama {BackgroundProcesses.MaxAge.TotalMinutes:0} menit).",
                              $"AIT stops it when the thread ends (after {BackgroundProcesses.MaxAge.TotalMinutes:0} minutes at most).");
            await progress.DetailAsync(Lang.T("🔐 menunggu izin developer", "🔐 waiting for the developer's permission"), cancellationToken, hang: hang);
            recorder.Log(ActivityLevel.Info, Lang.T($"Planner minta izin menjalankan di latar belakang: {items[0].Target}", $"The planner asks permission to start in the background: {items[0].Target}"));
            var approval = new PlannerApproval(new ApprovalRequest(items, message), Command: new PendingCommand(command, workingDirectory, reason, Background: true, Wait: wait));
            return new PlannerToolOutcome(string.Empty, null, Question: PlannerQuestion.ForApproval(approval));
        }

        return new PlannerToolOutcome(await LaunchAsync(command, workingDirectory, wait, input, prefix, recorder, progress, cancellationToken), null);
    }
}

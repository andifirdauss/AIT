using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // A read-only command runs at once; one that needs permission runs when a saved grant covers it, otherwise the
    // planner pauses with an approval request (the command runs when the developer allows it, RunApprovedCommandAsync).
    private async Task<PlannerToolOutcome> RunCommandAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var hang = Hang(prefix);
        if (!input.Settings.CommandToolEnabled)
        {
            await progress.DetailAsync(Lang.T("🟡 run_command dimatikan di pengaturan", "🟡 run_command turned off in the settings"), cancellationToken, hang: hang);
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

        var check = CommandPolicy.Classify(command, input.Scope, workingDirectory);
        switch (check.Verdict)
        {
            case CommandVerdict.Refused:
                recorder.Log(ActivityLevel.Warning, Lang.T($"Planner → run_command ditolak: {check.Reason}", $"Planner → run_command refused: {check.Reason}"));
                await progress.DetailAsync($"⛔ {check.Reason}", cancellationToken, hang: hang);
                return new PlannerToolOutcome($"Error: the command was refused: {check.Reason}.", null, true);

            case CommandVerdict.NeedsApproval:
                // The developer sees (and a grant keeps) the command with its secrets masked; a pattern that held a secret
                // is not offered.
                var items = (check.Items ?? []).Select(i => i.Kind == ApprovalKind.Command
                    ? i with { Target = secrets.Redact(i.Target), Pattern = i.Pattern is { } pattern && secrets.Redact(pattern) == pattern ? pattern : null }
                    : i).ToList();
                if (!await AllGrantedAsync(input, items, recorder, cancellationToken))
                {
                    // A tools node (prefix set) leaves the command to its planner, which can ask.
                    if (prefix.Length > 0)
                    {
                        recorder.Log(ActivityLevel.Info, Lang.T($"Tools node → run_command butuh izin, diserahkan ke planner: {items[0].Target}", $"Tools node → run_command needs permission, left to the planner: {items[0].Target}"));
                        await progress.DetailAsync(Lang.T("🔒 butuh izin developer — diserahkan ke planner", "🔒 needs the developer's permission — left to the planner"), cancellationToken, hang: hang);
                        return new PlannerToolOutcome($"Error: this command needs the developer's permission ({check.Reason}), which a sub agent cannot ask for. Do without it, or name the command in report_findings so the planner can run it.", null, true);
                    }

                    if (!input.CanApprove)
                    {
                        await progress.DetailAsync(Lang.T("🔒 butuh izin developer, tidak bisa ditanyakan di sini", "🔒 needs the developer's permission, which cannot be asked here"), cancellationToken, hang: hang);
                        return new PlannerToolOutcome($"Error: this command needs the developer's permission ({check.Reason}), and they cannot be asked from here. Do without it, or tell them in the summary which command to run themselves.", null, true);
                    }

                    var reason = Str(args, "reason");
                    var text = Lang.T("Planner ingin menjalankan perintah", "The planner wants to run a command") + (reason is null ? "." : $": {reason}") +
                               $"\n\n`{input.Scope.ToDisplay(workingDirectory)}` · " + Lang.T($"butuh izin karena: {check.Reason}", $"needs permission because: {check.Reason}");
                    await progress.DetailAsync(Lang.T("🔐 menunggu izin developer", "🔐 waiting for the developer's permission"), cancellationToken, hang: hang);
                    recorder.Log(ActivityLevel.Info, Lang.T($"Planner minta izin menjalankan: {items[0].Target}", $"The planner asks permission to run: {items[0].Target}"));
                    var approval = new PlannerApproval(new ApprovalRequest(items, text), Command: new PendingCommand(command, workingDirectory, reason));
                    return new PlannerToolOutcome(string.Empty, null, Question: PlannerQuestion.ForApproval(approval));
                }
                break;
        }

        return new PlannerToolOutcome(await ExecuteAsync(command, workingDirectory, input, prefix, recorder, progress, cancellationToken), null);
    }
}

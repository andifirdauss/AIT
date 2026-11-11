using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // commit_tasks: a batch of final tasks before the whole plan. It is validated together with the batches committed
    // before it (files as they were when committed), then fixed; with early execution its tasks start at once.
    private async Task<PlannerToolOutcome> CommitTasksAsync(string arguments, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (input.Stream is not { } stream)
            return new PlannerToolOutcome("Error: commit_tasks is not available here. Send the whole plan with submit_plan.", null, true);
        if (!ThreadPlan.TryParse(arguments, out var batch, out var parseError) || batch is null)
            return new PlannerToolOutcome($"Batch rejected: invalid JSON ({parseError}). Call commit_tasks again.", null, true);
        if (batch.Tasks.Count == 0)
            return new PlannerToolOutcome("Batch rejected: it has no tasks. Commit tasks, or call submit_plan when the plan is complete.", null, true);

        batch.RestorePaths(path => secrets.Restore(path).Text);
        // Those sections belong to the final plan only.
        batch.Answer = null;
        batch.Test = null;
        batch.Tests = null;
        batch.Publish = null;

        IReadOnlyList<PlanTask> added;
        using (await stream.LockAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(stream.Committed.Summary) && string.IsNullOrWhiteSpace(batch.Summary))
                return new PlannerToolOutcome("Batch rejected: \"summary\" is required with the first batch (one or two sentences on the whole change, in the developer's language).", null, true);

            var merged = stream.Merge(batch, out var errors);
            if (errors.Count == 0) errors.AddRange(PlanValidator.Validate(merged, input.Scope, existence: stream.Existence, earlier: stream.CommittedIds));
            if (errors.Count > 0)
            {
                recorder.Log(ActivityLevel.Warning, Lang.T($"Batch rencana ditolak validator: {string.Join("; ", errors)}", $"Plan batch rejected by the validator: {string.Join("; ", errors)}"));
                return new PlannerToolOutcome($"Batch rejected; nothing of it was committed. Fix these problems and call commit_tasks again:\n- {string.Join("\n- ", errors)}", null, true);
            }

            // A batch cannot pause the thread for a permission: folders outside the root must be allowed already.
            var newFolders = (merged.OutsideFolders ?? []).Where(f => !stream.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            var missing = new List<string>();
            foreach (var folder in newFolders)
                if (!await approvals.UseAsync(input.Project.ProjectId, ApprovalKind.WriteOutside, folder, recorder.Thread.Id, cancellationToken))
                    missing.Add(folder);
            if (missing.Count > 0)
                return new PlannerToolOutcome($"Batch rejected: its tasks change files outside the project root ({string.Join(", ", missing)}), which needs the developer's permission. Commit the other tasks, and send these with submit_plan (AIT asks the developer then).", null, true);

            stream.Order(merged);
            added = await stream.CommitAsync(merged, cancellationToken);
        }

        var running = stream.ExecutesEarly;
        var ids = string.Join(", ", added.Select(t => t.Id));
        var files = string.Join(", ", added.Select(t => $"{t.Id} {t.Target}"));
        await progress.StepAsync(Lang.T(
            $"📦 batch {stream.Batches} · {added.Count} tugas{(running ? " langsung dijalankan" : " menunggu rencana akhir")}: {files}",
            $"📦 batch {stream.Batches} · {added.Count} task(s){(running ? " running now" : " waiting for the final plan")}: {files}"), cancellationToken, hang: 3);
        recorder.Log(ActivityLevel.Info, Lang.T(
            $"Planner meng-commit batch {stream.Batches} ({added.Count} tugas: {ids}){(running ? "; eksekusi dimulai selagi planner lanjut" : string.Empty)}.",
            $"The planner committed batch {stream.Batches} ({added.Count} tasks: {ids}){(running ? "; execution started while the planner goes on" : string.Empty)}."));

        return new PlannerToolOutcome(
            $"Batch {stream.Batches} committed: {ids}" + (running ? " — executing now." : " — they run with the final plan.") +
            $" Committed so far: {stream.Committed.Tasks.Count} tasks, {stream.Committed.Contract.Count} contract items. Keep planning; send the remaining tasks with commit_tasks or submit_plan (only the tasks not committed yet).",
            null);
    }
}

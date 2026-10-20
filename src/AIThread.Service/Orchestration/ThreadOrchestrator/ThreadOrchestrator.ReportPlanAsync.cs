namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "📋 Rencana": the summary, then one entry per task (id, file and dependencies) with its title under it.
    private static async Task ReportPlanAsync(ClientProgress progress, ThreadPlan plan, CancellationToken cancellationToken)
    {
        await progress.PhaseAsync("📋", $"Rencana · {plan.Tasks.Count} tugas", cancellationToken);
        if (!string.IsNullOrWhiteSpace(plan.Summary)) await progress.TextAsync(plan.Summary, cancellationToken);
        for (var i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            var last = i == plan.Tasks.Count - 1;
            var dependencies = task.DependsOn is { Count: > 0 } deps ? $" ← {string.Join(", ", deps)}" : string.Empty;
            var indent = task.Id.Length + 1;
            await progress.StepAsync($"{task.Id} {task.Target}{dependencies}", cancellationToken, last, indent);
            await progress.DetailAsync(task.Title, cancellationToken, indent, underLast: last);
        }
    }
}

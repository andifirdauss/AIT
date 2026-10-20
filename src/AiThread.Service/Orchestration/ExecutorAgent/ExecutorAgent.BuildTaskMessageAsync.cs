using System.Text;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    private async Task<string> BuildTaskMessageAsync(ExecutorJob job, TextFileState file, CancellationToken cancellationToken)
    {
        var task = job.Task;
        var builder = new StringBuilder();

        // The plan section is the same for every task of the plan, so it comes first: the executors of one plan share
        // that prefix in the provider's prompt cache. Everything about this task follows it.
        builder.Append("# Plan (shared by every task)\n");
        builder.Append("## Contract (shared names — use exactly as written)\n");
        if (job.Plan.Contract.Count == 0) builder.Append("(none)\n");
        foreach (var item in job.Plan.Contract)
        {
            builder.Append($"- {item.Name}: {item.Value}");
            if (!string.IsNullOrWhiteSpace(item.Note)) builder.Append($" ({item.Note})");
            builder.Append('\n');
        }
        builder.Append('\n');

        if (!string.IsNullOrWhiteSpace(job.Plan.Summary))
            builder.Append("## Overall goal (context only — other tasks handle the other files)\n").Append(job.Plan.Summary.Trim()).Append("\n\n");

        builder.Append($"# Task {task.Id}: {task.Title}\n");
        builder.Append($"Action: {(task.IsCreate ? "create a new file" : "edit an existing file")}\n");
        builder.Append($"Target file: {task.File}\n\n");

        builder.Append("## Instruction\n").Append(task.Instruction.Trim()).Append("\n\n");

        var references = await BuildReferencesAsync(job, cancellationToken);
        if (references.Length > 0) builder.Append("## Reference excerpts (read-only, from other files)\n").Append(references).Append('\n');

        builder.Append(FileSection(task, file));
        builder.Append("\nRespond with SEARCH/REPLACE blocks and the SUMMARY line only.");
        return builder.ToString();
    }
}

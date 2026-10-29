using System.Text;
using System.Text.Json.Nodes;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    // A failed task becomes an incident of the Log menu with what the model saw and wrote: task.md (task, model,
    // errors per attempt), task-message.md (the task message as sent) and attempt-<n>-output.txt / -feedback.txt.
    // Returns the incident id.
    private string? RecordFailure(ExecutorJob job, JsonArray messages, IReadOnlyList<ExecutorAttempt> attempts, string error)
    {
        var task = job.Task;
        var summary = new StringBuilder();
        summary.Append("# Executor ").Append(task.Id).Append(" failed\n\n");
        summary.Append("- File: ").Append(task.File).Append(" (").Append(task.Action).Append(")\n");
        summary.Append("- Node: ").Append(job.Node.Name).Append(" · model ").Append(job.Node.Model).Append('\n');
        summary.Append("- Attempts: ").Append(attempts.Count).Append('\n');
        summary.Append("\n## Last error\n\n").Append(error).Append('\n');
        summary.Append("\n## Attempts\n\n| # | Model | Finish | Output chars |\n|---|---|---|---|\n");
        foreach (var attempt in attempts)
            summary.Append("| ").Append(attempt.Number).Append(" | ").Append(attempt.Model).Append(" | ").Append(attempt.FinishReason ?? "-")
                .Append(" | ").Append(attempt.Output.Length).Append(" |\n");
        summary.Append("\n## Instruction\n\n").Append(task.Instruction).Append('\n');

        var files = new List<DiagnosticAttachment> { new("task.md", summary.ToString()) };
        if (messages.Count > 1 && messages[1]?["content"]?.GetValue<string>() is { } taskMessage)
            files.Add(new DiagnosticAttachment("task-message.md", taskMessage));
        foreach (var attempt in attempts)
        {
            files.Add(new DiagnosticAttachment($"attempt-{attempt.Number}-output.txt", attempt.Output.Length == 0 ? "(empty reply)" : attempt.Output));
            files.Add(new DiagnosticAttachment($"attempt-{attempt.Number}-feedback.txt", attempt.Feedback));
        }

        return diagnostics.Record(new DiagnosticReport("Error", "executor", $"{task.Id} {task.File}: {Line(error)}", error,
            job.ThreadId, job.ProjectId, Files: files));
    }

    private static string Line(string text)
    {
        var line = text.Split('\n', 2)[0];
        return line.Length <= 200 ? line : line[..200] + "…";
    }
}

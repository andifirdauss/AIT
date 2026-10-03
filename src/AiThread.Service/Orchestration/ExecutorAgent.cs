using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Options;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Orchestration;

public sealed record ExecutorJob(
    ThreadPlan Plan,
    PlanTask Task,
    AiNode Node,
    WorkspaceScope Scope,
    int MaxRetries,
    Func<string, Task> OnOutput,
    Func<int, Task> OnAttempt);

public sealed record ExecutorOutcome(bool Success, string? Summary, string? Diff, string? Error, int Tokens, int Attempts);

// Cheap model, tiny scope: one file, the instruction, the contract and a few excerpts. Its SEARCH/REPLACE
// output is applied under a per-file lock against the file's current content; failures are fed back for a retry.
public sealed class ExecutorAgent(INineRouterService router, FileLockProvider fileLocks, NodePromptBuilder prompts, IOptions<AiThreadOptions> options)
{
    private const int ExcerptMargin = 40;
    private const int MaxContextLinesPerRef = 250;
    private const int MaxContextChars = 40_000;

    public async Task<ExecutorOutcome> ExecuteAsync(ExecutorJob job, CancellationToken cancellationToken)
    {
        var task = job.Task;
        if (!job.Scope.TryResolve(task.File, out var fullPath, out var pathError))
            return new ExecutorOutcome(false, null, null, pathError, 0, 0);

        var initial = await TextFileIo.ReadAsync(fullPath, cancellationToken);
        var messages = new JsonArray
        {
            ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Executor, job.Node, cancellationToken)),
            ChatMessages.User(await BuildTaskMessageAsync(job, initial, cancellationToken))
        };

        var tokens = 0;
        var maxAttempts = 1 + Math.Max(0, job.MaxRetries);
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await job.OnAttempt(attempt);

            ChatCompletionResult result;
            try
            {
                result = await router.ChatAsync(new ChatCompletionRequest
                {
                    Model = job.Node.Model,
                    Messages = messages,
                    Temperature = job.Node.Temperature ?? 0.1,
                    MaxTokens = job.Node.MaxTokens
                }, job.OnOutput, cancellationToken);
            }
            catch (NineRouterException ex)
            {
                return new ExecutorOutcome(false, null, null, ex.Message, tokens, attempt);
            }

            tokens += result.TotalTokens;
            var parsed = EditBlockParser.Parse(result.Content);

            string feedback;
            if (parsed.Blocks.Count == 0)
            {
                lastError = result.FinishReason == "length"
                    ? "Output was cut off (max tokens) before any complete edit block."
                    : "No valid SEARCH/REPLACE block in the output.";
                feedback = $"{lastError} Respond again using exactly the required format: edit blocks followed by one SUMMARY line.";
            }
            else
            {
                var applied = await ApplyAsync(job, fullPath, parsed, cancellationToken);
                if (applied.Outcome is { } done) return done with { Tokens = tokens, Attempts = attempt };

                lastError = string.Join(" ", applied.Errors);
                feedback = $"""
                    Your edit could not be applied, nothing was written:
                    - {string.Join("\n- ", applied.Errors)}

                    {await CurrentContentSectionAsync(job, fullPath, cancellationToken)}

                    Output corrected edit blocks for the whole task (not only the failed block), then the SUMMARY line.
                    """;
            }

            messages.Add(ChatMessages.Assistant(result.Content));
            messages.Add(ChatMessages.User(feedback));
        }

        return new ExecutorOutcome(false, null, null, lastError ?? "Executor gagal.", tokens, maxAttempts);
    }

    private async Task<(ExecutorOutcome? Outcome, IReadOnlyList<string> Errors)> ApplyAsync(
        ExecutorJob job, string fullPath, ParsedEdit parsed, CancellationToken cancellationToken)
    {
        // Other tasks may have edited this file since the executor read it; re-read under the lock.
        using var _ = await fileLocks.AcquireAsync(fullPath, cancellationToken);
        var current = await TextFileIo.ReadAsync(fullPath, cancellationToken);
        var applied = EditApplier.Apply(current, parsed.Blocks);
        if (!applied.Success) return (null, applied.Errors);

        await TextFileIo.WriteAsync(fullPath, applied.Content, current, CancellationToken.None);
        var diff = DiffBuilder.Build(job.Task.File, parsed.Blocks, applied.CreatesFile);
        var summary = parsed.Summary ?? $"{(applied.CreatesFile ? "Membuat" : "Mengubah")} {job.Task.File} ({parsed.Blocks.Count} blok).";
        return (new ExecutorOutcome(true, summary, diff, null, 0, 0), []);
    }

    private async Task<string> BuildTaskMessageAsync(ExecutorJob job, TextFileState file, CancellationToken cancellationToken)
    {
        var task = job.Task;
        var builder = new StringBuilder();
        builder.Append($"# Task {task.Id}: {task.Title}\n");
        builder.Append($"Action: {(task.IsCreate ? "create a new file" : "edit an existing file")}\n");
        builder.Append($"Target file: {task.File}\n\n");

        builder.Append("## Instruction\n").Append(task.Instruction.Trim()).Append("\n\n");

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

        var references = await BuildReferencesAsync(job, cancellationToken);
        if (references.Length > 0) builder.Append("## Reference excerpts (read-only, from other files)\n").Append(references).Append('\n');

        builder.Append(FileSection(task, file));
        builder.Append("\nRespond with SEARCH/REPLACE blocks and the SUMMARY line only.");
        return builder.ToString();
    }

    private async Task<string> CurrentContentSectionAsync(ExecutorJob job, string fullPath, CancellationToken cancellationToken) =>
        FileSection(job.Task, await TextFileIo.ReadAsync(fullPath, cancellationToken));

    private string FileSection(PlanTask task, TextFileState file)
    {
        var language = LanguageOf(task.File);
        if (!file.Exists)
            return $"## Current content of {task.File}\n(the file does not exist yet — create it with one block whose SEARCH section is empty)\n";

        var lines = WorkspaceTools.SplitLines(file.Content);
        var fullMax = options.Value.ExecutorFullFileMaxLines;
        if (lines.Length <= fullMax)
            return $"## Current content of {task.File}\n```{language}\n{file.Content.TrimEnd('\n')}\n```\n";

        // Large file: show the planner's focus ranges with a margin, marking what is hidden.
        var ranges = (task.Focus ?? [])
            .Where(r => r.StartLine > 0 && r.EndLine >= r.StartLine)
            .Select(r => (Start: Math.Max(1, r.StartLine - ExcerptMargin), End: Math.Min(lines.Length, r.EndLine + ExcerptMargin)))
            .OrderBy(r => r.Start)
            .ToList();
        if (ranges.Count == 0) ranges.Add((1, fullMax));

        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }

        var builder = new StringBuilder();
        builder.Append($"## Current content of {task.File} (excerpts — the file has {lines.Length} lines)\n");
        builder.Append("Only the excerpts below are shown. Copy SEARCH text from them; never include the \"lines ... not shown\" markers.\n");
        builder.Append($"```{language}\n");
        var cursor = 1;
        foreach (var (start, end) in merged)
        {
            if (start > cursor) builder.Append($"... (lines {cursor}-{start - 1} not shown) ...\n");
            for (var i = start; i <= end; i++) builder.Append(lines[i - 1]).Append('\n');
            cursor = end + 1;
        }
        if (cursor <= lines.Length) builder.Append($"... (lines {cursor}-{lines.Length} not shown) ...\n");
        builder.Append("```\n");
        return builder.ToString();
    }

    private static async Task<string> BuildReferencesAsync(ExecutorJob job, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var reference in job.Task.Context ?? [])
        {
            if (builder.Length >= MaxContextChars) break;
            // Read-only excerpts may come from outside the project root (e.g. a sibling project used as a model).
            if (!job.Scope.TryResolveForRead(reference.File, out var fullPath, out _) || !File.Exists(fullPath)) continue;

            var state = await TextFileIo.ReadAsync(fullPath, cancellationToken);
            var lines = WorkspaceTools.SplitLines(state.Content);
            if (lines.Length == 0) continue;

            var start = Math.Clamp(reference.StartLine ?? 1, 1, lines.Length);
            var end = Math.Clamp(reference.EndLine ?? lines.Length, start, lines.Length);
            if (end - start + 1 > MaxContextLinesPerRef) end = start + MaxContextLinesPerRef - 1;

            builder.Append($"### {job.Scope.ToDisplay(fullPath)} (lines {start}-{end})");
            if (!string.IsNullOrWhiteSpace(reference.Note)) builder.Append($" — {reference.Note}");
            builder.Append($"\n```{LanguageOf(fullPath)}\n");
            for (var i = start; i <= end; i++) builder.Append(lines[i - 1]).Append('\n');
            builder.Append("```\n");
        }

        return builder.Length <= MaxContextChars ? builder.ToString() : builder.ToString()[..MaxContextChars] + "\n... (references truncated)\n";
    }

    private static string LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".cshtml" or ".razor" => "cshtml",
        ".js" or ".mjs" or ".cjs" => "javascript",
        ".ts" or ".tsx" => "typescript",
        ".jsx" => "jsx",
        ".sql" => "sql",
        ".json" => "json",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".scss" => "scss",
        ".xml" or ".csproj" or ".config" or ".props" or ".targets" or ".slnx" => "xml",
        ".md" => "markdown",
        ".py" => "python",
        ".yml" or ".yaml" => "yaml",
        _ => "text"
    };
}

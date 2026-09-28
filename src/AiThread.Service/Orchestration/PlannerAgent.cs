using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed record PlannerInput(
    ThreadRequest Request,
    ProjectContext Project,
    AiNode Node,
    WorkspaceScope Scope,
    RuntimeSettings Settings);

public sealed record PlanningResult(ThreadPlan Plan, int Steps, int Tokens);

// Tool-using loop: the expensive model explores the repository (read-only) and ends by calling submit_plan.
public sealed class PlannerAgent(INineRouterService router, IMemoryService memoryService)
{
    private const int MaxPromptChars = 40_000;
    private const int MaxTextNudges = 2;

    public async Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var tools = new WorkspaceTools(input.Scope);
        var messages = new JsonArray
        {
            ChatMessages.System(AgentPrompts.WithNodeInstructions(AgentPrompts.Planner, input.Node.SystemPrompt)),
            ChatMessages.User(await BuildBriefAsync(input, tools, cancellationToken))
        };

        var maxSteps = input.Settings.PlannerMaxSteps;
        var tokens = 0;
        var textReplies = 0;

        for (var step = 1; step <= maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastStep = step == maxSteps;
            if (lastStep)
                messages.Add(ChatMessages.User("You have reached the investigation limit. Call submit_plan now with the best plan you can make from what you already know."));

            var result = await router.ChatAsync(new ChatCompletionRequest
            {
                Model = input.Node.Model,
                Messages = messages,
                Tools = lastStep ? PlannerTools.SubmitOnly() : PlannerTools.All(),
                Temperature = input.Node.Temperature ?? 0.2,
                MaxTokens = input.Node.MaxTokens
            }, cancellationToken: cancellationToken);
            tokens += result.TotalTokens;

            if (result.ToolCalls.Count == 0)
            {
                messages.Add(ChatMessages.Assistant(result.Content));

                // Some models ignore tool calling and print the plan as JSON.
                if (ThreadPlan.TryParseFromText(result.Content, out var textPlan) && textPlan is not null)
                {
                    var errors = PlanValidator.Validate(textPlan, input.Scope);
                    if (errors.Count == 0) return new PlanningResult(textPlan, step, tokens);
                    messages.Add(ChatMessages.User($"The plan is invalid:\n- {string.Join("\n- ", errors)}\nFix it and call submit_plan."));
                    continue;
                }

                if (++textReplies > MaxTextNudges)
                {
                    // The model insists on answering in prose: treat it as a direct answer.
                    recorder.Log(ActivityLevel.Warning, "Planner tidak memanggil submit_plan; jawaban teks dipakai sebagai jawaban langsung.");
                    return new PlanningResult(new ThreadPlan { Summary = "Jawaban langsung dari planner.", Answer = result.Content }, step, tokens);
                }

                messages.Add(ChatMessages.User("Do not answer in plain text. Use the tools to investigate if needed, then call submit_plan (put the full answer in its \"answer\" field when no file change is needed)."));
                continue;
            }

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));

            var outcomes = await Task.WhenAll(result.ToolCalls.Select(call => RunToolAsync(call, input, tools, recorder, progress, cancellationToken)));

            ThreadPlan? accepted = null;
            for (var i = 0; i < result.ToolCalls.Count; i++)
            {
                messages.Add(ChatMessages.Tool(result.ToolCalls[i].Id, outcomes[i].Output));
                accepted ??= outcomes[i].AcceptedPlan;
            }

            if (accepted is not null) return new PlanningResult(accepted, step, tokens);
        }

        throw new OrchestrationException($"Planner tidak menghasilkan rencana yang valid dalam {maxSteps} langkah.");
    }

    private async Task<ToolOutcome> RunToolAsync(
        ToolCall call,
        PlannerInput input,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            return new ToolOutcome($"Error: arguments are not valid JSON ({ex.Message}). Call the tool again with a JSON object.", null);
        }

        if (call.Name != PlannerTools.SubmitPlan)
        {
            var detail = Describe(call.Name, args);
            recorder.PlannerActivity(call.Name, detail);
            recorder.Log(ActivityLevel.Debug, $"Planner → {call.Name} {detail}");
            await progress.LineAsync($"   · {call.Name} {detail}", cancellationToken);
        }

        try
        {
            switch (call.Name)
            {
                case PlannerTools.ListDirectory:
                    return new ToolOutcome(tools.ListDirectory(Str(args, "path") ?? ".", Int(args, "depth") ?? 2), null);

                case PlannerTools.FindFiles:
                    return new ToolOutcome(tools.FindFiles(Str(args, "pattern") ?? string.Empty), null);

                case PlannerTools.SearchText:
                    return new ToolOutcome(tools.SearchText(Str(args, "query") ?? string.Empty, Str(args, "glob"), Bool(args, "regex")), null);

                case PlannerTools.ReadFile:
                    return new ToolOutcome(await tools.ReadFileAsync(Str(args, "path") ?? string.Empty, Int(args, "startLine"), Int(args, "endLine"), cancellationToken), null);

                case PlannerTools.SearchMemory:
                    var entries = await memoryService.GetIndexAsync(input.Project.ProjectId, Str(args, "query"), 25, cancellationToken);
                    return new ToolOutcome(entries.Count == 0 ? "No matching memories." : FormatIndex(entries), null);

                case PlannerTools.ReadMemory:
                    var ids = (args["ids"] as JsonArray ?? [])
                        .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : (Guid?)null)
                        .Where(g => g is not null).Select(g => g!.Value).ToList();
                    var memories = await memoryService.GetManyAsync(ids, cancellationToken);
                    return new ToolOutcome(memories.Count == 0
                        ? "No memories found for those ids."
                        : string.Join("\n\n", memories.Select(m => $"### {m.Label} ({m.Type}, {m.Level})\n{m.Description}")), null);

                case PlannerTools.SubmitPlan:
                    return SubmitPlan(call.Arguments, input.Scope, recorder);

                default:
                    return new ToolOutcome($"Error: unknown tool '{call.Name}'.", null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ToolOutcome($"Error: {ex.Message}", null);
        }
    }

    private static ToolOutcome SubmitPlan(string arguments, WorkspaceScope scope, ThreadRecorder recorder)
    {
        if (!ThreadPlan.TryParse(arguments, out var plan, out var parseError) || plan is null)
            return new ToolOutcome($"Plan rejected: invalid JSON ({parseError}). Call submit_plan again.", null);

        var errors = PlanValidator.Validate(plan, scope);
        if (errors.Count > 0)
        {
            recorder.Log(ActivityLevel.Warning, $"Rencana ditolak validator: {string.Join("; ", errors)}");
            return new ToolOutcome($"Plan rejected. Fix these problems and call submit_plan again:\n- {string.Join("\n- ", errors)}", null);
        }

        return new ToolOutcome("Plan accepted.", plan);
    }

    private async Task<string> BuildBriefAsync(PlannerInput input, WorkspaceTools tools, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var project = input.Project;
        var builder = new StringBuilder();

        builder.Append("# Developer request\n");
        builder.Append(Limit(request.PromptContext ?? request.Prompt, MaxPromptChars)).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(request.ConversationContext))
        {
            builder.Append("# Earlier conversation (oldest first, truncated)\n");
            builder.Append(request.ConversationContext).Append("\n\n");
        }

        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        builder.Append($"Solution: {(project.SolutionPath is null ? "(none — ad-hoc workspace)" : input.Scope.ToRelative(project.SolutionPath))}\n");
        if (project.SolutionProjects.Count > 0)
        {
            builder.Append("Projects in solution:\n");
            foreach (var p in project.SolutionProjects.Take(60)) builder.Append("- ").Append(p).Append('\n');
        }
        builder.Append("All tool paths are relative to the root.\n\n");

        builder.Append("## Structure (depth 2)\n");
        builder.Append(tools.ListDirectory(".", 2, 200)).Append('\n');

        builder.Append("# Memory index (labels only — call read_memory with ids to read the details)\n");
        var relevant = await memoryService.GetIndexAsync(project.ProjectId, request.Prompt, 20, cancellationToken);
        var recent = await memoryService.GetIndexAsync(project.ProjectId, null, 10, cancellationToken);
        var index = relevant.Concat(recent.Where(r => r.Level == MemoryLevel.Short)).DistinctBy(e => e.Id).Take(25).ToList();
        builder.Append(index.Count == 0 ? "(empty)\n" : FormatIndex(index));

        return builder.ToString();
    }

    private static string FormatIndex(IEnumerable<Common.DTOs.MemoryIndexEntry> entries) =>
        string.Concat(entries.Select(e =>
            $"- {e.Id} [{(e.Level == MemoryLevel.Short ? "short" : "long")}/{e.Type.ToString().ToLowerInvariant()}, {e.CreatedAt:yyyy-MM-dd}] {e.Label}\n"));

    private static string Describe(string tool, JsonObject args) => tool switch
    {
        PlannerTools.ReadFile => $"`{Str(args, "path")}`" + (Int(args, "startLine") is { } s ? $" ({s}-{Int(args, "endLine")?.ToString() ?? "…"})" : string.Empty),
        PlannerTools.ListDirectory => $"`{Str(args, "path") ?? "."}`",
        PlannerTools.FindFiles => $"`{Str(args, "pattern")}`",
        PlannerTools.SearchText => $"\"{Str(args, "query")}\"" + (Str(args, "glob") is { } g ? $" in `{g}`" : string.Empty),
        PlannerTools.SearchMemory => $"\"{Str(args, "query")}\"",
        PlannerTools.ReadMemory => $"{(args["ids"] as JsonArray)?.Count ?? 0} item",
        _ => string.Empty
    };

    private static string? Str(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static int? Int(JsonObject args, string name)
    {
        if (args[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }

    private static bool Bool(JsonObject args, string name) =>
        args[name] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) && parsed);

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max] + "\n... (truncated)";

    private sealed record ToolOutcome(string Output, ThreadPlan? AcceptedPlan);
}

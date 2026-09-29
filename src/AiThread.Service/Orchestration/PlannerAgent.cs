using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Planner;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Nodes are the active planners in priority order; the first one plans, the rest take over when it is rate limited or down.
public sealed record PlannerInput(
    ThreadRequest Request,
    ProjectContext Project,
    IReadOnlyList<AiNode> Nodes,
    WorkspaceScope Scope,
    RuntimeSettings Settings);

// Node is the planner that finished the plan (a fallback when the preferred one failed).
public sealed record PlanningResult(ThreadPlan Plan, int Steps, int Tokens, AiNode Node);

public sealed record PlannerToolOutcome(string Output, ThreadPlan? AcceptedPlan, bool IsError = false);

// The planner node at NodeIndex cannot serve right now (rate limit, outage, CLI failure); the next one may take over.
public sealed class PlannerUnavailableException(int nodeIndex, string reason, string message, Exception? innerException = null)
    : OrchestrationException(message, innerException)
{
    public int NodeIndex { get; } = nodeIndex;

    public string Reason { get; } = reason;
}

// The expensive model explores the repository (read-only) and ends by calling submit_plan, either through a 9router
// tool-calling loop or through the vendor's Claude Code CLI.
public sealed class PlannerAgent(
    INineRouterService router,
    IMemoryService memoryService,
    NodeConcurrencyLimiter limiter,
    ClaudeCliPlanner cliPlanner,
    ContextKeeper contextKeeper)
{
    private const int MaxPromptChars = 40_000;
    private const int MaxTextNudges = 2;

    public async Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        var tools = new WorkspaceTools(input.Scope);
        var brief = await BuildBriefAsync(input, tools, cancellationToken);

        for (var index = 0; ;)
        {
            try
            {
                return input.Nodes[index].Backend == NodeBackend.ClaudeCli
                    ? await PlanWithCliAsync(input, index, brief, tools, recorder, progress, cancellationToken)
                    : await PlanWithRouterAsync(input, index, brief, tools, recorder, progress, cancellationToken);
            }
            catch (PlannerUnavailableException ex) when (ex.NodeIndex + 1 < input.Nodes.Count)
            {
                // A different backend cannot continue the conversation, so the next planner starts from the brief again.
                index = ex.NodeIndex + 1;
                await AnnounceSwitchAsync(input.Nodes[ex.NodeIndex], input.Nodes[index], ex.Reason, ex.Message, recorder, progress, cancellationToken);
            }
        }
    }

    private async Task<PlanningResult> PlanWithRouterAsync(
        PlannerInput input,
        int nodeIndex,
        string brief,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var node = input.Nodes[nodeIndex];
        var messages = new JsonArray { SystemMessage(node), ChatMessages.User(brief) };

        var maxSteps = input.Settings.PlannerMaxSteps;
        var tokens = 0;
        var textReplies = 0;

        for (var step = 1; step <= maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastStep = step == maxSteps;
            if (lastStep)
                messages.Add(ChatMessages.User("You have reached the investigation limit. Call submit_plan now with the best plan you can make from what you already know."));

            ChatCompletionResult result;
            while (true)
            {
                try
                {
                    // A slot is held only while the model answers, not while tools run, so concurrent threads share the planner.
                    using (await limiter.AcquireAsync(node, cancellationToken))
                    {
                        result = await router.ChatAsync(new ChatCompletionRequest
                        {
                            Model = node.Model,
                            Messages = messages,
                            Tools = lastStep ? PlannerTools.SubmitOnly() : PlannerTools.All(),
                            Temperature = node.Temperature ?? 0.2,
                            MaxTokens = node.MaxTokens
                        }, cancellationToken: cancellationToken);
                    }
                    break;
                }
                catch (NineRouterException ex) when (ex.IsModelUnavailable)
                {
                    var nextIndex = nodeIndex + 1;
                    if (nextIndex >= input.Nodes.Count || input.Nodes[nextIndex].Backend != NodeBackend.Router)
                        throw new PlannerUnavailableException(nodeIndex, FailureReason(ex), ex.Message, ex);

                    // The conversation so far is plain OpenAI chat, so the next 9router planner continues where this one stopped.
                    var next = input.Nodes[nextIndex];
                    await AnnounceSwitchAsync(node, next, FailureReason(ex), ex.Message, recorder, progress, cancellationToken);
                    nodeIndex = nextIndex;
                    node = next;
                    messages[0] = SystemMessage(node);
                }
            }
            tokens += result.TotalTokens;

            if (result.ToolCalls.Count == 0)
            {
                messages.Add(ChatMessages.Assistant(result.Content));

                // Some models ignore tool calling and print the plan as JSON.
                if (ThreadPlan.TryParseFromText(result.Content, out var textPlan) && textPlan is not null)
                {
                    var errors = PlanValidator.Validate(textPlan, input.Scope);
                    if (errors.Count == 0) return new PlanningResult(textPlan, step, tokens, node);
                    messages.Add(ChatMessages.User($"The plan is invalid:\n- {string.Join("\n- ", errors)}\nFix it and call submit_plan."));
                    continue;
                }

                if (++textReplies > MaxTextNudges)
                    return DirectAnswer(result.Content, step, tokens, node, recorder);

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

            if (accepted is not null) return new PlanningResult(accepted, step, tokens, node);
        }

        throw new OrchestrationException($"Planner tidak menghasilkan rencana yang valid dalam {maxSteps} langkah.");
    }

    private async Task<PlanningResult> PlanWithCliAsync(
        PlannerInput input,
        int nodeIndex,
        string brief,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var node = input.Nodes[nodeIndex];
        var prompt = $"{AgentPrompts.WithNodeInstructions(AgentPrompts.Planner, node.SystemPrompt)}\n\n{ClaudeCliPlanner.ToolNotes}\n\n{brief}";

        CliPlanningResult result;
        try
        {
            // The whole CLI run is one call from the limiter's point of view.
            using (await limiter.AcquireAsync(node, cancellationToken))
            {
                result = await cliPlanner.PlanAsync(
                    node,
                    prompt,
                    input.Project.RootPath,
                    // CLI turns count every tool round trip; there is no forced final step, so leave some headroom.
                    input.Settings.PlannerMaxSteps * 2,
                    (call, token) => RunToolAsync(call, input, tools, recorder, progress, token),
                    use => ReportCliToolAsync(use, input.Scope, recorder, progress, cancellationToken),
                    cancellationToken);
            }
        }
        catch (ClaudeCliException ex)
        {
            throw new PlannerUnavailableException(nodeIndex, "Claude Code CLI gagal", ex.Message, ex);
        }

        if (result.Plan is not null) return new PlanningResult(result.Plan, result.Turns, result.Tokens, node);

        // The CLI ended without submit_plan: accept a plan printed as JSON, otherwise use the text as a direct answer.
        if (ThreadPlan.TryParseFromText(result.Text, out var textPlan) && textPlan is not null && PlanValidator.Validate(textPlan, input.Scope).Count == 0)
            return new PlanningResult(textPlan, result.Turns, result.Tokens, node);
        if (!string.IsNullOrWhiteSpace(result.Text))
            return DirectAnswer(result.Text, result.Turns, result.Tokens, node, recorder);

        throw new PlannerUnavailableException(nodeIndex, "Claude Code CLI gagal", "Claude Code CLI selesai tanpa rencana maupun jawaban.");
    }

    // Built-in CLI tools show up in the progress like the 9router planner's tools; MCP calls are reported by RunToolAsync.
    private static async Task ReportCliToolAsync(ClaudeCliToolUse use, WorkspaceScope scope, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (use.Name.StartsWith("mcp__", StringComparison.Ordinal)) return;

        var path = Str(use.Input, "file_path") ?? Str(use.Input, "path");
        if (path is not null && Path.IsPathRooted(path) && scope.IsInside(Path.GetFullPath(path))) path = scope.ToRelative(Path.GetFullPath(path));

        var where = Str(use.Input, "glob") ?? path;
        var detail = use.Name switch
        {
            "Read" => $"`{path}`" + (Int(use.Input, "offset") is { } offset ? $" (dari baris {offset})" : string.Empty),
            "Grep" => $"\"{Str(use.Input, "pattern")}\"" + (where is not null ? $" in `{where}`" : string.Empty),
            "Glob" => $"`{Str(use.Input, "pattern")}`" + (path is not null ? $" in `{path}`" : string.Empty),
            _ => string.Empty
        };

        recorder.PlannerActivity(use.Name, detail);
        recorder.Log(ActivityLevel.Debug, $"Planner (CLI) → {use.Name} {detail}");
        await progress.LineAsync($"   · {use.Name} {detail}", cancellationToken);
    }

    private static PlanningResult DirectAnswer(string text, int steps, int tokens, AiNode node, ThreadRecorder recorder)
    {
        // The model insists on answering in prose: treat it as a direct answer.
        recorder.Log(ActivityLevel.Warning, "Planner tidak memanggil submit_plan; jawaban teks dipakai sebagai jawaban langsung.");
        return new PlanningResult(new ThreadPlan { Summary = "Jawaban langsung dari planner.", Answer = text }, steps, tokens, node);
    }

    private static async Task AnnounceSwitchAsync(AiNode from, AiNode to, string reason, string message, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        recorder.Log(ActivityLevel.Warning, $"Planner `{Label(from)}` gagal ({message}); dialihkan ke `{Label(to)}`.");
        await progress.LineAsync($"   ⚠️ Planner `{Label(from)}` tidak tersedia ({reason}), beralih ke `{Label(to)}`…", cancellationToken);
    }

    private static string Label(AiNode node) => node.Backend == NodeBackend.ClaudeCli ? $"claude-cli:{node.Model}" : node.Model;

    private async Task<PlannerToolOutcome> RunToolAsync(
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
            return new PlannerToolOutcome($"Error: arguments are not valid JSON ({ex.Message}). Call the tool again with a JSON object.", null, true);
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
                    return new PlannerToolOutcome(tools.ListDirectory(Str(args, "path") ?? ".", Int(args, "depth") ?? 2), null);

                case PlannerTools.FindFiles:
                    return new PlannerToolOutcome(tools.FindFiles(Str(args, "pattern") ?? string.Empty), null);

                case PlannerTools.SearchText:
                    return new PlannerToolOutcome(tools.SearchText(Str(args, "query") ?? string.Empty, Str(args, "glob"), Bool(args, "regex")), null);

                case PlannerTools.ReadFile:
                    return new PlannerToolOutcome(await tools.ReadFileAsync(Str(args, "path") ?? string.Empty, Int(args, "startLine"), Int(args, "endLine"), cancellationToken), null);

                case PlannerTools.SearchMemory:
                    var entries = await memoryService.GetIndexAsync(input.Project.ProjectId, Str(args, "query"), 25, cancellationToken);
                    return new PlannerToolOutcome(entries.Count == 0 ? "No matching memories." : FormatIndex(entries), null);

                case PlannerTools.ReadMemory:
                    var ids = (args["ids"] as JsonArray ?? [])
                        .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : (Guid?)null)
                        .Where(g => g is not null).Select(g => g!.Value).ToList();
                    var memories = await memoryService.GetManyAsync(ids, cancellationToken);
                    return new PlannerToolOutcome(memories.Count == 0
                        ? "No memories found for those ids."
                        : string.Join("\n\n", memories.Select(m => $"### {m.Label} ({m.Type}, {m.Level})\n{m.Description}")), null);

                case PlannerTools.ReadThread:
                    return new PlannerToolOutcome(await contextKeeper.ReadThreadAsync(input.Project.ProjectId, Str(args, "id") ?? string.Empty, cancellationToken), null);

                case PlannerTools.SubmitPlan:
                    return SubmitPlan(call.Arguments, input.Scope, recorder);

                default:
                    return new PlannerToolOutcome($"Error: unknown tool '{call.Name}'.", null, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }

    private static PlannerToolOutcome SubmitPlan(string arguments, WorkspaceScope scope, ThreadRecorder recorder)
    {
        if (!ThreadPlan.TryParse(arguments, out var plan, out var parseError) || plan is null)
            return new PlannerToolOutcome($"Plan rejected: invalid JSON ({parseError}). Call submit_plan again.", null, true);

        var errors = PlanValidator.Validate(plan, scope);
        if (errors.Count > 0)
        {
            recorder.Log(ActivityLevel.Warning, $"Rencana ditolak validator: {string.Join("; ", errors)}");
            return new PlannerToolOutcome($"Plan rejected. Fix these problems and call submit_plan again:\n- {string.Join("\n- ", errors)}", null, true);
        }

        return new PlannerToolOutcome("Plan accepted.", plan);
    }

    private async Task<string> BuildBriefAsync(PlannerInput input, WorkspaceTools tools, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var project = input.Project;
        var builder = new StringBuilder();

        builder.Append("# Developer request\n");
        builder.Append(Limit(request.PromptContext ?? request.Prompt, MaxPromptChars)).Append("\n\n");

        // With a context pack the planner sees the summary plus only the turns the summary does not cover yet.
        var pack = request.Session?.Pack;
        if (!string.IsNullOrWhiteSpace(pack))
        {
            builder.Append("# Session so far (summary by the context keeper; read_thread gives the full record of a thread)\n");
            builder.Append(pack.Trim()).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationContext))
        {
            builder.Append(string.IsNullOrWhiteSpace(pack) ? "# Earlier conversation (oldest first, truncated)\n" : "# Conversation since that summary (oldest first, truncated)\n");
            builder.Append(request.ConversationContext).Append("\n\n");
        }

        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        builder.Append($"Solution: {input.Scope.ToRelative(project.SolutionPath)}\n");
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

    private static JsonObject SystemMessage(AiNode node) =>
        ChatMessages.System(AgentPrompts.WithNodeInstructions(AgentPrompts.Planner, node.SystemPrompt));

    private static string FailureReason(NineRouterException ex) => ex.StatusCode switch
    {
        429 => "rate limit 429",
        408 => "timeout",
        { } code => $"HTTP {code}",
        null => "tidak dapat dihubungi"
    };

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
        PlannerTools.ReadThread => $"`{Str(args, "id")}`",
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
}

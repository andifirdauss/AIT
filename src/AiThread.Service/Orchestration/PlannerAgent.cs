using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Nodes are the active planners in priority order; the first one plans, and its fallback chain (AiNode.FallbackNodeId)
// takes over when it is rate limited or down. CanAsk offers ask_user: the developer can answer (setting on, and a session the answer can come back through).
public sealed record PlannerInput(
    ThreadRequest Request,
    ProjectContext Project,
    IReadOnlyList<AiNode> Nodes,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    bool CanAsk = false);

// Node is the planner that finished (a fallback when the preferred one failed). Plan is null when the planner paused
// on a question instead (Pause). Steps and tokens are totals for the thread, including the run before a pause.
// MemoriesUsed counts the memories (and wiki pages) the planner read in this run.
public sealed record PlanningResult(ThreadPlan? Plan, int Steps, int Tokens, int CachedTokens, AiNode Node, PlannerPause? Pause = null, int MemoriesUsed = 0);

public sealed record PlannerPause(PlannerQuestion Question, PlannerConversation Conversation);

public sealed record PlannerToolOutcome(string Output, ThreadPlan? AcceptedPlan, bool IsError = false, PlannerQuestion? Question = null);

// The expensive model explores the repository (read-only) and ends by calling submit_plan, through a 9router
// tool-calling loop. The planner's fallback node continues the same conversation when it is rate limited or down.
public sealed class PlannerAgent(
    NodeCaller caller,
    IMemoryService memoryService,
    ContextKeeper contextKeeper,
    BuildRunner buildRunner,
    NodePromptBuilder prompts)
{
    private const int MaxRequestChars = 40_000;
    private const int MaxAttachedContextChars = 30_000;
    private const int MaxTextNudges = 2;
    private const int MaxQuestions = 2;

    // A resumed run always gets a few steps to act on the answer, even when the question came late.
    private const int StepsAfterAnswer = 3;

    public async Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        var tools = new WorkspaceTools(input.Scope);
        var build = input.Settings.BuildEnabled ? BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, null, out _) : null;
        var brief = await BuildBriefAsync(input, tools, build, cancellationToken);

        var run = new PlannerRun
        {
            Messages = [await SystemMessageAsync(input.Nodes[0], cancellationToken), ChatMessages.User(brief, input.Request.Images)],
            Node = input.Nodes[0],
            CanBuild = build is not null,
            MaxSteps = input.Settings.PlannerMaxSteps
        };
        return await RunAsync(input, run, tools, recorder, progress, cancellationToken);
    }

    // Continues a conversation paused by ask_user: the developer's reply becomes the result of that tool call.
    public async Task<PlanningResult> ResumeAsync(
        PlannerInput input,
        PlannerConversation conversation,
        string toolResult,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        // The planner that asked, when it is still active; otherwise the preferred one takes the conversation over.
        var node = input.Nodes.FirstOrDefault(n => n.Id == conversation.NodeId) ?? input.Nodes[0];
        var messages = conversation.Messages;
        messages[0] = await SystemMessageAsync(node, cancellationToken);
        messages.Add(ChatMessages.Tool(conversation.AskCallId, toolResult));

        var run = new PlannerRun
        {
            Messages = messages,
            Node = node,
            Step = conversation.Step,
            Tokens = conversation.Tokens,
            CachedTokens = conversation.CachedTokens,
            QuestionsAsked = conversation.QuestionsAsked,
            CanBuild = conversation.CanBuild,
            MaxSteps = Math.Max(input.Settings.PlannerMaxSteps, conversation.Step + StepsAfterAnswer)
        };
        return await RunAsync(input, run, new WorkspaceTools(input.Scope), recorder, progress, cancellationToken);
    }

    private async Task<PlanningResult> RunAsync(
        PlannerInput input,
        PlannerRun run,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var messages = run.Messages;
        var node = run.Node;
        var textReplies = 0;

        // The same tool list on every step: changing it invalidates the provider's prompt cache for the whole
        // conversation. Only a planner that ignored the "submit now" message gets a submit-only extra step.
        var allTools = PlannerTools.All(run.CanBuild, input.CanAsk);

        while (run.Step < run.MaxSteps + 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = ++run.Step;
            var forcedStep = step > run.MaxSteps;
            if (step == run.MaxSteps)
                messages.Add(ChatMessages.User("You have reached the investigation limit. Call submit_plan now with the best plan you can make from what you already know."));

            // A slot is held only while the model answers, not while tools run, so concurrent threads share the planner.
            // The conversation so far is plain OpenAI chat, so a fallback planner continues where this one stopped.
            var current = node;
            var reply = await caller.ChatAsync(current, async candidate =>
            {
                if (candidate.Id != current.Id) messages[0] = await SystemMessageAsync(candidate, cancellationToken);
                return new ChatCompletionRequest
                {
                    Model = candidate.Model,
                    Messages = messages,
                    Tools = forcedStep ? PlannerTools.SubmitOnly() : allTools,
                    Temperature = candidate.Temperature ?? 0.2,
                    MaxTokens = candidate.MaxTokens
                };
            }, cancellationToken, onSwitch: s => s.ReportAsync("Planner", recorder, progress, cancellationToken));
            node = run.Node = reply.Node;
            var result = reply.Result;
            run.Tokens += result.TotalTokens;
            run.CachedTokens += result.CachedTokens;

            if (result.ToolCalls.Count == 0)
            {
                messages.Add(ChatMessages.Assistant(result.Content));

                // Some models ignore tool calling and print the plan as JSON.
                if (ThreadPlan.TryParseFromText(result.Content, out var textPlan) && textPlan is not null)
                {
                    var errors = PlanValidator.Validate(textPlan, input.Scope);
                    if (errors.Count == 0) return Finished(textPlan, run, node);
                    messages.Add(ChatMessages.User($"The plan is invalid:\n- {string.Join("\n- ", errors)}\nFix it and call submit_plan."));
                    continue;
                }

                if (++textReplies > MaxTextNudges)
                    return DirectAnswer(result.Content, run, node, recorder);

                messages.Add(ChatMessages.User("Do not answer in plain text. Use the tools to investigate if needed, then call submit_plan (put the full answer in its \"answer\" field when no file change is needed)."));
                continue;
            }

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));

            var outcomes = await Task.WhenAll(result.ToolCalls.Select(call => RunToolAsync(call, input, run, tools, recorder, progress, cancellationToken)));

            ThreadPlan? accepted = null;
            (ToolCall Call, PlannerQuestion Question)? ask = null;
            for (var i = 0; i < result.ToolCalls.Count; i++)
            {
                var call = result.ToolCalls[i];
                if (outcomes[i].Question is { } question)
                {
                    // The answer becomes this call's result when the thread resumes.
                    if (ask is null)
                    {
                        ask = (call, question);
                        continue;
                    }
                    messages.Add(ChatMessages.Tool(call.Id, "Error: only one question at a time; this one was not shown to the developer."));
                    continue;
                }
                messages.Add(ChatMessages.Tool(call.Id, outcomes[i].Output));
                accepted ??= outcomes[i].AcceptedPlan;
            }

            if (accepted is not null) return Finished(accepted, run, node);
            if (ask is { } pause)
            {
                run.QuestionsAsked++;
                var conversation = new PlannerConversation(messages, node.Id, run.Step, run.Tokens, run.CachedTokens, pause.Call.Id, run.QuestionsAsked, run.CanBuild);
                return new PlanningResult(null, run.Step, run.Tokens, run.CachedTokens, node, new PlannerPause(pause.Question, conversation), run.MemoriesRead.Count);
            }
        }

        throw new OrchestrationException($"Planner tidak menghasilkan rencana yang valid dalam {run.MaxSteps} langkah.");
    }

    private static PlanningResult Finished(ThreadPlan plan, PlannerRun run, AiNode node) =>
        new(plan, run.Step, run.Tokens, run.CachedTokens, node, MemoriesUsed: run.MemoriesRead.Count);

    private static PlanningResult DirectAnswer(string text, PlannerRun run, AiNode node, ThreadRecorder recorder)
    {
        // The model insists on answering in prose: treat it as a direct answer.
        recorder.Log(ActivityLevel.Warning, "Planner tidak memanggil submit_plan; jawaban teks dipakai sebagai jawaban langsung.");
        return Finished(new ThreadPlan { Summary = "Jawaban langsung dari planner.", Answer = text }, run, node);
    }

    private static PlannerToolOutcome AskUser(JsonObject args, PlannerInput input, PlannerRun run)
    {
        if (!input.CanAsk)
            return new PlannerToolOutcome("Error: the developer cannot be asked here. Decide yourself and state your assumption in the summary.", null, true);
        if (run.Step >= run.MaxSteps)
            return new PlannerToolOutcome("Error: no steps left for a question. Call submit_plan with your best plan and state your assumptions in the summary.", null, true);
        if (run.QuestionsAsked >= MaxQuestions)
            return new PlannerToolOutcome($"Error: you already asked {run.QuestionsAsked} questions. Decide yourself and state your assumption in the summary.", null, true);

        var question = Str(args, "question");
        if (question is null) return new PlannerToolOutcome("Error: \"question\" is required.", null, true);

        var options = (args["options"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(o => (Label: Str(o, "label"), Description: Str(o, "description")))
            .Where(o => o.Label is not null)
            .Take(4)
            .Select(o => new QuestionOption(o.Label!, o.Description))
            .ToList();
        return new PlannerToolOutcome(string.Empty, null, Question: new PlannerQuestion(question, options));
    }

    private async Task<PlannerToolOutcome> RunToolAsync(
        ToolCall call,
        PlannerInput input,
        PlannerRun run,
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
            // run_build writes its own lines: the command it resolved, then the result.
            if (call.Name != PlannerTools.RunBuild)
                await progress.StepAsync(ClientProgress.Pad(call.Name, PlannerTools.NameColumn) + detail, cancellationToken, hang: PlannerTools.NameColumn);
        }

        try
        {
            switch (call.Name)
            {
                case PlannerTools.ListDirectory:
                    return new PlannerToolOutcome(tools.ListDirectory(Str(args, "path") ?? ".", Int(args, "depth") ?? 2), null);

                case PlannerTools.FindFiles:
                    return new PlannerToolOutcome(tools.FindFiles(Str(args, "pattern") ?? string.Empty, Str(args, "path")), null);

                case PlannerTools.SearchText:
                    return new PlannerToolOutcome(tools.SearchText(Str(args, "query") ?? string.Empty, Str(args, "glob"), Bool(args, "regex"), path: Str(args, "path")), null);

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
                    lock (run.MemoriesRead) run.MemoriesRead.UnionWith(memories.Select(m => m.Id));
                    return new PlannerToolOutcome(memories.Count == 0
                        ? "No memories found for those ids."
                        : string.Join("\n\n", memories.Select(m => $"### {m.Label} ({m.Type}, {m.Level})\n{m.Description}")), null);

                case PlannerTools.ReadThread:
                    return new PlannerToolOutcome(await contextKeeper.ReadThreadAsync(input.Project.ProjectId, Str(args, "id") ?? string.Empty, cancellationToken), null);

                case PlannerTools.RunBuild:
                    return await RunBuildAsync(input, Str(args, "target"), recorder, progress, cancellationToken);

                case PlannerTools.AskUser:
                    return AskUser(args, input, run);

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

    // Two progress lines: the command as soon as it is known, the result under it when the build is done.
    private async Task<PlannerToolOutcome> RunBuildAsync(PlannerInput input, string? target, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var head = ClientProgress.Pad(PlannerTools.RunBuild, PlannerTools.NameColumn);
        if (!input.Settings.BuildEnabled)
        {
            await progress.StepAsync(head + (target ?? string.Empty), cancellationToken, hang: PlannerTools.NameColumn);
            await progress.DetailAsync("🟡 build dimatikan di setting", cancellationToken, PlannerTools.NameColumn);
            return new PlannerToolOutcome("Error: building is disabled in the AIT settings.", null, true);
        }

        var command = BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, target, out var error);
        if (command is null)
        {
            await progress.StepAsync(head + (target ?? string.Empty), cancellationToken, hang: PlannerTools.NameColumn);
            await progress.DetailAsync($"❌ {error}", cancellationToken, PlannerTools.NameColumn);
            return new PlannerToolOutcome($"Error: {error}", null, true);
        }

        await progress.StepAsync(head + command.Display, cancellationToken, hang: PlannerTools.NameColumn);
        var result = await buildRunner.RunAsync(command, TimeSpan.FromSeconds(input.Settings.BuildTimeoutSeconds), cancellationToken);
        recorder.Log(result.Passed ? ActivityLevel.Info : ActivityLevel.Warning, $"Planner → run_build `{command.Display}`: {BuildReport.Headline(result)}");
        await progress.DetailAsync($"{(result.Passed ? "✅" : "❌")} {BuildReport.Headline(result)}", cancellationToken, PlannerTools.NameColumn);
        return new PlannerToolOutcome(BuildReport.Format(result, input.Scope), null);
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

    private async Task<string> BuildBriefAsync(PlannerInput input, WorkspaceTools tools, BuildCommand? build, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var project = input.Project;
        var builder = new StringBuilder();

        // Ordered from the most stable part to the most specific one: providers that cache prompt prefixes reuse the
        // project section across the threads of a project, and the request itself comes last, after the material it
        // refers to. Each part keeps its head and tail when clipped.
        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        if (project.Stacks.Count > 0) builder.Append($"Stack: {string.Join(", ", project.Stacks)}\n");
        if (FocusPath(input.Scope, request.WorkingDirectory) is { } focus)
            builder.Append($"Session opened in: {focus} (relative to the root; the request most likely concerns this part)\n");
        if (project.Modules.Count > 0)
        {
            builder.Append("Manifests found (solution, build and package files):\n");
            foreach (var module in project.Modules.Take(60)) builder.Append("- ").Append(module).Append('\n');
        }
        builder.Append(build is null
            ? "Build: AIT cannot build this project; mention build or test steps in the summary.\n"
            : $"Build: `{build.Display}` (run_build). AIT builds it again after the executors finish{(input.Settings.MaxFixRounds > 0 ? " and has the errors fixed" : string.Empty)}.\n");
        builder.Append("Tool paths are relative to the root. The read tools also accept absolute paths outside the root, for read-only checks; file changes are only possible inside the root.\n\n");

        builder.Append("## Structure (depth 2)\n");
        builder.Append(tools.ListDirectory(".", 2, 200)).Append("\n\n");

        // Claude Code sends injected context (CLAUDE.md, reminders, attached files) in the same turn as the request.
        var attached = AttachedContext(request.PromptContext, request.Prompt);
        if (attached is not null)
        {
            builder.Append("# Context sent with the request (reference only; act on the developer request at the end)\n");
            builder.Append(Limit(attached, MaxAttachedContextChars)).Append("\n\n");
        }

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

        builder.Append("# Memory index (labels only — call read_memory with ids to read the details)\n");
        var relevant = await memoryService.GetIndexAsync(project.ProjectId, request.Prompt, 20, cancellationToken);
        var recent = await memoryService.GetIndexAsync(project.ProjectId, null, 10, cancellationToken);
        var index = relevant.Concat(recent.Where(r => r.Level is MemoryLevel.Short or MemoryLevel.Business)).DistinctBy(e => e.Id).Take(25).ToList();
        builder.Append(index.Count == 0 ? "(empty)\n" : FormatIndex(index)).Append('\n');

        builder.Append("# Developer request\n");
        builder.Append(Limit(request.Prompt, MaxRequestChars)).Append('\n');

        return builder.ToString();
    }

    // Where inside the project the session was opened, when that is below the root (a repository root can hold several apps).
    private static string? FocusPath(WorkspaceScope scope, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !scope.TryResolve(workingDirectory, out var full, out _)) return null;
        var relative = scope.ToRelative(full);
        return relative is "." or "" ? null : relative;
    }

    // The planner can read a skill's supporting files with its read tools.
    private async Task<JsonObject> SystemMessageAsync(AiNode node, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Planner, node, cancellationToken, canReadFiles: true));

    private static string FormatIndex(IEnumerable<Common.DTOs.MemoryIndexEntry> entries) =>
        string.Concat(entries.Select(e =>
            $"- {e.Id} [{e.Level.ToString().ToLowerInvariant()}/{e.Type.ToString().ToLowerInvariant()}, {e.CreatedAt:yyyy-MM-dd}] {e.Label}\n"));

    private static string Describe(string tool, JsonObject args) => tool switch
    {
        PlannerTools.ReadFile => $"`{Str(args, "path")}`" + (Int(args, "startLine") is { } s ? $" ({s}-{Int(args, "endLine")?.ToString() ?? "…"})" : string.Empty),
        PlannerTools.ListDirectory => $"`{Str(args, "path") ?? "."}`",
        PlannerTools.FindFiles => $"`{Str(args, "pattern")}`" + (Str(args, "path") is { } findIn ? $" in `{findIn}`" : string.Empty),
        PlannerTools.SearchText => $"\"{Str(args, "query")}\"" + (Str(args, "glob") is { } g ? $" in `{g}`" : string.Empty) + (Str(args, "path") is { } searchIn ? $" under `{searchIn}`" : string.Empty),
        PlannerTools.SearchMemory => $"\"{Str(args, "query")}\"",
        PlannerTools.ReadMemory => $"{(args["ids"] as JsonArray)?.Count ?? 0} item",
        PlannerTools.ReadThread => $"`{Str(args, "id")}`",
        PlannerTools.RunBuild => Str(args, "target") is { } target ? $"`{target}`" : string.Empty,
        PlannerTools.AskUser => Str(args, "question") is { } question ? $"\"{(question.Length > 120 ? question[..120] + "…" : question)}\"" : string.Empty,
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

    // Keeps the head and the tail: the developer's instruction usually sits at the end of a long message.
    private static string Limit(string text, int max)
    {
        if (text.Length <= max) return text;
        var head = max / 3;
        var tail = max - head;
        return text[..head] + $"\n... ({text.Length - max} chars truncated) ...\n" + text[^tail..];
    }

    private static string? AttachedContext(string? promptContext, string prompt)
    {
        if (string.IsNullOrWhiteSpace(promptContext)) return null;
        var context = prompt.Length > 0 ? promptContext.Replace(prompt, string.Empty) : promptContext;
        return string.IsNullOrWhiteSpace(context) ? null : context.Trim();
    }

    // Step is the last step taken; MaxSteps is when the planner is told to submit (one forced step may follow).
    private sealed class PlannerRun
    {
        public required JsonArray Messages { get; init; }
        public required AiNode Node { get; set; }
        public int Step { get; set; }
        public int Tokens { get; set; }
        public int CachedTokens { get; set; }
        public int QuestionsAsked { get; set; }
        public bool CanBuild { get; init; }
        public int MaxSteps { get; init; }
        public HashSet<Guid> MemoriesRead { get; } = [];
    }
}

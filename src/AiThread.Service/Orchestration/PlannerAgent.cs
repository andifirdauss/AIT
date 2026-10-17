using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Nodes are the active planners in priority order; the first one plans, and its fallback chain (AiNode.FallbackNodeId)
// takes over when it is rate limited or down. CanAsk offers ask_user: the developer can answer (setting on, and a session the answer can come back through).
// CodeMap: module and layer of the files the request names, as far as known before planning (the first search layer).
public sealed record PlannerInput(
    ThreadRequest Request,
    ProjectContext Project,
    IReadOnlyList<AiNode> Nodes,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    bool CanAsk = false,
    IReadOnlyList<Memory.CodeMapHit>? CodeMap = null);

// Node is the planner that finished (a fallback when the preferred one failed). Plan is null when the planner paused
// on a question instead (Pause). Steps and tokens are totals for the thread, including the run before a pause.
// MemoriesUsed counts the memories (and wiki pages) the planner read in this run.
// ToolsNodeTokens: spent by the tools nodes the planner delegated tools to (not part of Tokens).
public sealed record PlanningResult(ThreadPlan? Plan, int Steps, int Tokens, int CachedTokens, AiNode Node, PlannerPause? Pause = null, int MemoriesUsed = 0, int ToolsNodeTokens = 0);

public sealed record PlannerPause(PlannerQuestion Question, PlannerConversation Conversation);

public sealed record PlannerToolOutcome(string Output, ThreadPlan? AcceptedPlan, bool IsError = false, PlannerQuestion? Question = null);

// The expensive model explores the repository (read-only) and ends by calling submit_plan, through a 9router
// tool-calling loop. The planner's fallback node continues the same conversation when it is rate limited or down.
// Tools the planner node delegated (AiNode.ToolDelegations) are run by tools nodes behind investigate instead.
public sealed class PlannerAgent(
    NodeCaller caller,
    IMemoryService memoryService,
    INodeService nodeService,
    PlannerToolRunner runner,
    ToolsNodeAgent toolsNodeAgent,
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
        var groups = await ToolsNodeGroupsAsync(input.Nodes[0], build is not null, cancellationToken);

        var run = new PlannerRun
        {
            Messages = [await SystemMessageAsync(input.Nodes[0], groups, cancellationToken), ChatMessages.User(brief, input.Request.Images)],
            Node = input.Nodes[0],
            CanBuild = build is not null,
            MaxSteps = input.Settings.PlannerMaxSteps,
            Groups = groups
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
        var groups = await ToolsNodeGroupsAsync(node, conversation.CanBuild, cancellationToken);
        var messages = conversation.Messages;
        messages[0] = await SystemMessageAsync(node, groups, cancellationToken);
        messages.Add(ChatMessages.Tool(conversation.AskCallId, toolResult));

        var run = new PlannerRun
        {
            Messages = messages,
            Node = node,
            Step = conversation.Step,
            Tokens = conversation.Tokens,
            CachedTokens = conversation.CachedTokens,
            ToolsNodeTokens = conversation.ToolsNodeTokens,
            QuestionsAsked = conversation.QuestionsAsked,
            CanBuild = conversation.CanBuild,
            MaxSteps = Math.Max(input.Settings.PlannerMaxSteps, conversation.Step + StepsAfterAnswer),
            Groups = groups
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
        var allTools = PlannerTools.All(run.CanBuild, input.CanAsk, run.Groups);

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
                if (candidate.Id != current.Id) messages[0] = await SystemMessageAsync(candidate, run.Groups, cancellationToken);
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
                var conversation = new PlannerConversation(messages, node.Id, run.Step, run.Tokens, run.CachedTokens, pause.Call.Id, run.QuestionsAsked, run.CanBuild, run.ToolsNodeTokens);
                return new PlanningResult(null, run.Step, run.Tokens, run.CachedTokens, node, new PlannerPause(pause.Question, conversation), run.MemoriesRead.Count, run.ToolsNodeTokens);
            }
        }

        throw new OrchestrationException($"Planner tidak menghasilkan rencana yang valid dalam {run.MaxSteps} langkah.");
    }

    private static PlanningResult Finished(ThreadPlan plan, PlannerRun run, AiNode node) =>
        new(plan, run.Step, run.Tokens, run.CachedTokens, node, MemoriesUsed: run.MemoriesRead.Count, ToolsNodeTokens: run.ToolsNodeTokens);

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

        var question = PlannerToolRunner.Str(args, "question");
        if (question is null) return new PlannerToolOutcome("Error: \"question\" is required.", null, true);

        var options = (args["options"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(o => (Label: PlannerToolRunner.Str(o, "label"), Description: PlannerToolRunner.Str(o, "description")))
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
        var args = PlannerToolRunner.ParseArguments(call.Arguments, out var error);
        if (args is null) return error!;

        // A delegated tool is not in the planner's list; a model that calls it anyway is pointed to investigate.
        if (run.Groups.FirstOrDefault(g => g.Tools.Contains(call.Name)) is { } holder)
            return new PlannerToolOutcome($"Error: {call.Name} is delegated to a sub agent{(run.Groups.Count > 1 ? $" (\"{holder.Key}\")" : string.Empty)}. Call investigate with your question instead.", null, true);

        // investigate numbers its own line; submit_plan has none.
        if (call.Name is not (PlannerTools.SubmitPlan or PlannerTools.Investigate))
            await PlannerToolRunner.AnnounceAsync(call.Name, args, "Planner", string.Empty, recorder, progress, cancellationToken);

        return call.Name switch
        {
            PlannerTools.Investigate => await InvestigateAsync(args, input, run, tools, recorder, progress, cancellationToken),
            PlannerTools.AskUser => AskUser(args, input, run),
            PlannerTools.SubmitPlan => SubmitPlan(call.Arguments, input.Scope, recorder),
            _ => await runner.RunAsync(call.Name, args, input, tools, run.MemoriesRead, string.Empty, recorder, progress, cancellationToken)
        };
    }

    // The tools node's tool calls appear under "#n" lines, since investigations run in parallel.
    private async Task<PlannerToolOutcome> InvestigateAsync(
        JsonObject args,
        PlannerInput input,
        PlannerRun run,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (run.Groups.Count == 0) return new PlannerToolOutcome("Error: there are no sub agents.", null, true);
        var question = PlannerToolRunner.Str(args, "question");
        if (question is null) return new PlannerToolOutcome("Error: \"question\" is required.", null, true);
        var agent = PlannerToolRunner.Str(args, "agent");
        var group = run.Groups.Count == 1 ? run.Groups[0] : run.Groups.FirstOrDefault(g => string.Equals(g.Key, agent, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return new PlannerToolOutcome($"Error: \"agent\" must be one of: {string.Join(", ", run.Groups.Select(g => g.Key))}.", null, true);

        var label = $"#{Interlocked.Increment(ref run.Investigations)}";
        var detail = $"{label} {(run.Groups.Count > 1 ? $"[{group.Key}] " : string.Empty)}{PlannerToolRunner.Describe(PlannerTools.Investigate, args)}";
        recorder.PlannerActivity(PlannerTools.Investigate, detail);
        recorder.Log(ActivityLevel.Debug, $"Planner → investigate {label} ({group.Node.Name}): {question}");
        await progress.StepAsync(ClientProgress.Pad(PlannerTools.Investigate, PlannerTools.NameColumn) + detail, cancellationToken, hang: PlannerTools.NameColumn);

        try
        {
            var result = await toolsNodeAgent.InvestigateAsync(group, question, input, tools, run.MemoriesRead, label, recorder, progress, cancellationToken);
            Interlocked.Add(ref run.ToolsNodeTokens, result.Tokens);
            await progress.StepAsync($"{label} ✅ {result.Steps} langkah · {result.Tokens} token · {result.Node.Model}", cancellationToken, hang: label.Length + 3);
            return new PlannerToolOutcome(result.Findings, null);
        }
        catch (NineRouterException ex) when (!cancellationToken.IsCancellationRequested)
        {
            recorder.Log(ActivityLevel.Warning, $"Tools node '{group.Node.Name}' gagal menjawab investigasi {label}: {ex.Message}");
            await progress.StepAsync($"{label} ❌ tools node {group.Node.Model} gagal: {ex.Message}", cancellationToken, hang: label.Length + 3);
            return new PlannerToolOutcome($"Error: the sub agent is unavailable ({ex.Message}). Try again later or plan with what you already know.", null, true);
        }
    }

    // Active tools nodes only (an inactive one gives its tools back to the planner), loaded with their skills. run_build
    // stays out when AIT cannot build the project, as it does for the planner.
    private async Task<IReadOnlyList<ToolsNodeGroup>> ToolsNodeGroupsAsync(AiNode planner, bool canBuild, CancellationToken cancellationToken)
    {
        var groups = new List<ToolsNodeGroup>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byToolsNode = planner.ToolDelegations
            .Where(d => PlannerTools.Delegable.Contains(d.Tool) && (canBuild || d.Tool != PlannerTools.RunBuild))
            .GroupBy(d => d.ToolsNodeId)
            .OrderBy(g => g.Min(d => PlannerTools.Delegable.IndexOf(d.Tool)));
        foreach (var delegations in byToolsNode)
        {
            var toolsNode = await nodeService.GetNodeAsync(delegations.Key, cancellationToken);
            if (toolsNode is not { IsActive: true, Role: NodeRole.ToolsNode }) continue;
            var tools = delegations.Select(d => d.Tool).OrderBy(t => PlannerTools.Delegable.IndexOf(t)).ToList();
            groups.Add(new ToolsNodeGroup(UniqueKey(toolsNode.Name, keys), toolsNode, tools));
        }
        return groups;
    }

    // A short identifier from the node name for investigate's agent enum: "Scout Murah" → "scout-murah".
    private static string UniqueKey(string name, HashSet<string> taken)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length == 0) slug = "sub";
        var key = slug;
        for (var i = 2; !taken.Add(key); i++) key = $"{slug}-{i}";
        return key;
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
            builder.Append(PlannerToolRunner.Limit(attached, MaxAttachedContextChars)).Append("\n\n");
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

        if (input.CodeMap is { Count: > 0 } codeMap)
        {
            builder.Append("# Code map of the files the request names (module · layer)\n");
            foreach (var hit in codeMap.Take(30))
                builder.Append($"- {hit.Path}: {(hit.Area is null ? string.Empty : hit.Area + " · ")}{hit.Module} · {hit.Layer}\n");
            builder.Append('\n');
        }

        var summary = await memoryService.GetModuleSummaryAsync(project.ProjectId, cancellationToken);
        if (summary.Count > 0)
        {
            builder.Append("# Memory map (labeled memories per module: layers | kinds)\n");
            builder.Append(PlannerToolRunner.FormatModuleMap(summary)).Append('\n');
        }

        builder.Append("# Memory index (labels only — call read_memory with ids to read the details)\n");
        var focusModules = input.CodeMap?.Select(h => h.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var relevant = await memoryService.SearchAsync(new MemorySearch(project.ProjectId, Text: request.Prompt, Take: 20, Modules: focusModules), cancellationToken);
        var recent = await memoryService.GetIndexAsync(project.ProjectId, null, 10, cancellationToken);
        var index = relevant.Concat(recent.Where(r => r.Level is MemoryLevel.Short or MemoryLevel.Business)).DistinctBy(e => e.Id).Take(25).ToList();
        builder.Append(index.Count == 0 ? "(empty)\n" : PlannerToolRunner.FormatIndex(index)).Append('\n');

        builder.Append("# Developer request\n");
        builder.Append(PlannerToolRunner.Limit(request.Prompt, MaxRequestChars)).Append('\n');

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
    private async Task<JsonObject> SystemMessageAsync(AiNode node, IReadOnlyList<ToolsNodeGroup> groups, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(
            groups.Count == 0 ? AgentPrompts.Planner : AgentPrompts.Planner + AgentPrompts.PlannerDelegation(groups), node, cancellationToken, canReadFiles: true));

    private static string? AttachedContext(string? promptContext, string prompt)
    {
        if (string.IsNullOrWhiteSpace(promptContext)) return null;
        var context = prompt.Length > 0 ? promptContext.Replace(prompt, string.Empty) : promptContext;
        return string.IsNullOrWhiteSpace(context) ? null : context.Trim();
    }

    // Step is the last step taken; MaxSteps is when the planner is told to submit (one forced step may follow).
    // Investigations and ToolsNodeTokens are fields: parallel investigate calls update them with Interlocked.
    private sealed class PlannerRun
    {
        public int Investigations;
        public int ToolsNodeTokens;

        public required JsonArray Messages { get; init; }
        public required AiNode Node { get; set; }
        public int Step { get; set; }
        public int Tokens { get; set; }
        public int CachedTokens { get; set; }
        public int QuestionsAsked { get; set; }
        public bool CanBuild { get; init; }
        public int MaxSteps { get; init; }
        public IReadOnlyList<ToolsNodeGroup> Groups { get; init; } = [];
        public HashSet<Guid> MemoriesRead { get; } = [];
    }
}

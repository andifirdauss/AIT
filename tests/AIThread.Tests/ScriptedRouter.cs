using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public enum ScriptedAgent
{
    Planner,
    ToolsNode,
    Executor,
    Reviewer,
    Tester,
    TesterTriage,
    UnitTestLabeler,
    Wrapper,
    MemoryExtractor,
    BusinessRouter,
    BusinessWriter,
    CodeMapper,
    MemoryRelabeler,
    ModuleSummary,
    ModuleDuplicates,
    FunctionCalls,
    ImpersonationDetector,
    Other
}

// Messages is a snapshot: the agents keep appending to the array they sent. ToolNames: the tools offered in the call.
public sealed record RouterCall(ScriptedAgent Agent, string Model, JsonArray Messages, IReadOnlyList<string>? ToolNames = null)
{
    public string System => Messages[0]?["content"]?.GetValue<string>() ?? string.Empty;

    public string LastUser => Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "user")["content"]!.ToString();
}

// Stands in for 9router: answers each agent from a script, recognising the agent by its role prompt. Executor replies
// are keyed by target file because executors run in parallel. Memory extraction, the code map, relabeling, the
// duplicate module check and unit test labels answer "nothing" unless scripted.
public sealed class ScriptedRouter : INineRouterService
{
    private readonly ConcurrentDictionary<ScriptedAgent, ConcurrentQueue<Func<RouterCall, ChatCompletionResult>>> _replies = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<RouterCall, ChatCompletionResult>>> _executorReplies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RouterCall> _calls = new();
    private int _toolCallIds;

    public IReadOnlyList<RouterCall> Calls => _calls.ToList();

    public IReadOnlyList<RouterCall> CallsTo(ScriptedAgent agent) => _calls.Where(c => c.Agent == agent).ToList();

    public RouterStatusDto? LastStatus => null;

    public ScriptedRouter On(ScriptedAgent agent, Func<RouterCall, ChatCompletionResult> reply)
    {
        _replies.GetOrAdd(agent, _ => new ConcurrentQueue<Func<RouterCall, ChatCompletionResult>>()).Enqueue(reply);
        return this;
    }

    public ScriptedRouter On(ScriptedAgent agent, ChatCompletionResult reply) => On(agent, _ => reply);

    public ScriptedRouter OnExecutor(string file, Func<RouterCall, ChatCompletionResult> reply)
    {
        _executorReplies.GetOrAdd(file, _ => new ConcurrentQueue<Func<RouterCall, ChatCompletionResult>>()).Enqueue(reply);
        return this;
    }

    public ScriptedRouter OnExecutor(string file, ChatCompletionResult reply) => OnExecutor(file, _ => reply);

    public async Task<ChatCompletionResult> ChatAsync(ChatCompletionRequest request, Func<string, Task>? onDelta = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = new RouterCall(Identify(request), request.Model, (JsonArray)request.Messages.DeepClone(),
            request.Tools?.Select(t => t?["function"]?["name"]?.GetValue<string>() ?? string.Empty).ToList());
        _calls.Enqueue(call);

        var reply = NextReply(call)
                    ?? call.Agent switch
                    {
                        ScriptedAgent.MemoryExtractor => _ => Text("""{"entry": null}"""),
                        ScriptedAgent.MemoryRelabeler => _ => Text("""{"memories": []}"""),
                        ScriptedAgent.ModuleSummary => _ => Text("""{"features": []}"""),
                        ScriptedAgent.CodeMapper => _ => Text("""{"groups": []}"""),
                        ScriptedAgent.ModuleDuplicates => _ => Text("""{"merges": []}"""),
                        ScriptedAgent.FunctionCalls => _ => Text("""{"functions": []}"""),
                        ScriptedAgent.UnitTestLabeler => _ => Text("""{"tests": []}"""),
                        ScriptedAgent.ImpersonationDetector => _ => Text("""{"available": false}"""),
                        _ => throw new InvalidOperationException($"Unscripted {call.Agent} call (model {call.Model}).")
                    };
        var result = reply(call);
        if (onDelta is not null && result.Content.Length > 0) await onDelta(result.Content);
        return result;
    }

    public Task<RouterStatusDto> CheckStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<RouterStatusDto> TestConnectionAsync(string? baseUrl, string? apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RouterModelDto>>([]);

    public Task<IReadOnlyList<RouterQuotaDto>> GetQuotasAsync(IReadOnlyCollection<string> models, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RouterQuotaDto>>([]);

    public Task<HttpResponseMessage> ForwardAsync(string pathAndQuery, string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public static ChatCompletionResult Text(string content, int tokens = 10) => new(content, [], "stop", tokens, tokens);

    public ChatCompletionResult Tool(string name, object arguments, int tokens = 10) => Tools(tokens, (name, arguments));

    public ChatCompletionResult Tools(int tokens, params (string Name, object Arguments)[] calls) =>
        new(string.Empty,
            calls.Select(c => new ToolCall($"call_{Interlocked.Increment(ref _toolCallIds)}", c.Name, JsonSerializer.Serialize(c.Arguments, ThreadPlan.JsonOptions))).ToList(),
            "tool_calls", tokens, tokens);

    public ChatCompletionResult SubmitPlan(ThreadPlan plan, int tokens = 10) => Tool(PlannerTools.SubmitPlan, plan, tokens);

    public static ChatCompletionResult Edit(string search, string replace, string summary, int tokens = 10) =>
        Text($"""
            <<<<<<< SEARCH
            {search}
            =======
            {replace}
            >>>>>>> REPLACE
            SUMMARY: {summary}
            """, tokens);

    public static ChatCompletionResult Create(string content, string summary, int tokens = 10) =>
        Text($"""
            <<<<<<< SEARCH
            =======
            {content}
            >>>>>>> REPLACE
            SUMMARY: {summary}
            """, tokens);

    private Func<RouterCall, ChatCompletionResult>? NextReply(RouterCall call)
    {
        if (call.Agent == ScriptedAgent.Executor)
        {
            var file = TargetFile(call.Messages);
            return file is not null && _executorReplies.TryGetValue(file, out var byFile) && byFile.TryDequeue(out var fileReply) ? fileReply : null;
        }
        return _replies.TryGetValue(call.Agent, out var queue) && queue.TryDequeue(out var reply) ? reply : null;
    }

    // The executor's first user message names the file ("Target file: x"); retries append to the same conversation.
    private static string? TargetFile(JsonArray messages)
    {
        var first = messages.OfType<JsonObject>().FirstOrDefault(m => m["role"]?.GetValue<string>() == "user")?["content"]?.ToString();
        var line = first?.Split('\n').FirstOrDefault(l => l.StartsWith("Target file: ", StringComparison.Ordinal));
        return line?["Target file: ".Length..].Trim();
    }

    private static ScriptedAgent Identify(ChatCompletionRequest request)
    {
        var system = request.Messages[0]?["content"]?.GetValue<string>() ?? string.Empty;
        if (system.StartsWith(AgentPrompts.Planner, StringComparison.Ordinal)) return ScriptedAgent.Planner;
        if (system.StartsWith(AgentPrompts.ToolsNode, StringComparison.Ordinal)) return ScriptedAgent.ToolsNode;
        if (system.StartsWith(AgentPrompts.Executor, StringComparison.Ordinal)) return ScriptedAgent.Executor;
        if (system.StartsWith(AgentPrompts.Reviewer, StringComparison.Ordinal)) return ScriptedAgent.Reviewer;
        if (system.StartsWith(AgentPrompts.Tester, StringComparison.Ordinal)) return ScriptedAgent.Tester;
        if (system.StartsWith(AgentPrompts.TesterTriage, StringComparison.Ordinal)) return ScriptedAgent.TesterTriage;
        if (system.StartsWith(AgentPrompts.UnitTestLabeler, StringComparison.Ordinal)) return ScriptedAgent.UnitTestLabeler;
        if (system.StartsWith(AgentPrompts.Wrapper, StringComparison.Ordinal)) return ScriptedAgent.Wrapper;
        if (system.StartsWith(AgentPrompts.MemoryExtractor, StringComparison.Ordinal)) return ScriptedAgent.MemoryExtractor;
        if (system.StartsWith(AgentPrompts.BusinessRouter, StringComparison.Ordinal)) return ScriptedAgent.BusinessRouter;
        if (system.StartsWith(AgentPrompts.BusinessWriter, StringComparison.Ordinal)) return ScriptedAgent.BusinessWriter;
        if (system.StartsWith(AgentPrompts.CodeMapper, StringComparison.Ordinal)) return ScriptedAgent.CodeMapper;
        if (system.StartsWith(AgentPrompts.MemoryRelabeler, StringComparison.Ordinal)) return ScriptedAgent.MemoryRelabeler;
        if (system.StartsWith(AgentPrompts.ModuleSummary, StringComparison.Ordinal)) return ScriptedAgent.ModuleSummary;
        if (system.StartsWith(AgentPrompts.ModuleDuplicates, StringComparison.Ordinal)) return ScriptedAgent.ModuleDuplicates;
        if (system.StartsWith(AgentPrompts.FunctionCalls, StringComparison.Ordinal)) return ScriptedAgent.FunctionCalls;
        if (system.StartsWith(AgentPrompts.ImpersonationDetector, StringComparison.Ordinal)) return ScriptedAgent.ImpersonationDetector;
        return ScriptedAgent.Other;
    }
}

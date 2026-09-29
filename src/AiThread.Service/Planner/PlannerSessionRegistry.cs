using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace AiThread.Service.Planner;

public sealed record McpToolResult(string Text, bool IsError);

// Live CLI planner runs. Two lookups:
//   - MCP sessions by URL token, so /mcp/planner/{token} reaches the tools of the right thread;
//   - Claude Code session ids of spawned CLIs, so the proxy can refuse a child whose requests loop back into AiThread.
public sealed class PlannerSessionRegistry
{
    private readonly ConcurrentDictionary<string, PlannerMcpSession> _mcpSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cliSessions = new(StringComparer.OrdinalIgnoreCase);

    public PlannerMcpSession Open(JsonArray tools, Func<string, string, CancellationToken, Task<McpToolResult>> invoke, CancellationToken runToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var session = new PlannerMcpSession(token, tools, invoke, runToken, () => _mcpSessions.TryRemove(token, out _));
        _mcpSessions[token] = session;
        return session;
    }

    public bool TryGet(string token, out PlannerMcpSession session) => _mcpSessions.TryGetValue(token, out session!);

    public IDisposable RegisterCliSession(string sessionId)
    {
        _cliSessions[sessionId] = 0;
        return new Release(() => _cliSessions.TryRemove(sessionId, out _));
    }

    public bool IsCliSession(string? sessionId) => sessionId is not null && _cliSessions.ContainsKey(sessionId);

    private sealed class Release(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) release();
        }
    }
}

public sealed class PlannerMcpSession(
    string token,
    JsonArray tools,
    Func<string, string, CancellationToken, Task<McpToolResult>> invoke,
    CancellationToken runToken,
    Action onDispose) : IDisposable
{
    private int _disposed;

    public string Token { get; } = token;

    public JsonArray Tools { get; } = tools;

    // Ends with the planner run as well as with the HTTP request that carries the call.
    public async Task<McpToolResult> InvokeAsync(string name, string arguments, CancellationToken requestToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, requestToken);
        return await invoke(name, arguments, linked.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) onDispose();
    }
}

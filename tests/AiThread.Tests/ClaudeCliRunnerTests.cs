using System.Text.RegularExpressions;
using AiThread.Common.Options;
using AiThread.Service.Orchestration;
using AiThread.Service.Planner;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiThread.Tests;

// Runs a fake `claude.cmd` that prints stream-json events, so process start (via cmd.exe), argument quoting,
// stdin and event parsing are covered without the real CLI.
public class ClaudeCliRunnerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly PlannerSessionRegistry _registry = new();

    [Fact]
    public async Task Runs_a_cmd_shim_in_a_path_with_spaces_and_parses_the_stream()
    {
        if (!OperatingSystem.IsWindows()) return;

        var script = WriteScript("""
            @echo off
            echo %* > "%~dp0args.txt"
            more > nul
            echo {"type":"system","subtype":"init"}
            echo {"type":"assistant","message":{"content":[{"type":"text","text":"looking"},{"type":"tool_use","id":"t1","name":"Grep","input":{"pattern":"UserService"}}]}}
            echo {"type":"result","subtype":"success","is_error":false,"result":"pong","num_turns":2,"usage":{"input_tokens":10,"cache_read_input_tokens":5,"output_tokens":3}}
            """);
        var uses = new List<ClaudeCliToolUse>();

        var result = await Runner(script).RunAsync(
            new ClaudeCliRun("opus", "hello", _workspace.Root, ["--max-turns", "3", "--mcp-config", Path.Combine(_workspace.Root, "a b", "mcp.json")], TimeSpan.FromMinutes(1)),
            use => { uses.Add(use); return Task.CompletedTask; },
            null,
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("pong", result.Text);
        Assert.Equal(2, result.Turns);
        Assert.Equal(18, result.Tokens);
        Assert.Equal("Grep", Assert.Single(uses).Name);

        var args = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(script)!, "args.txt"));
        Assert.Contains("-p --output-format stream-json --verbose --model opus", args);
        Assert.Contains("--setting-sources project", args);
        Assert.Contains($"--mcp-config \"{Path.Combine(_workspace.Root, "a b", "mcp.json")}\"", args);

        // The child's session id is only registered while it runs.
        var sessionId = Regex.Match(args, "--session-id ([0-9a-f-]{36})").Groups[1].Value;
        Assert.NotEmpty(sessionId);
        Assert.False(_registry.IsCliSession(sessionId));
    }

    [Fact]
    public async Task Error_result_is_reported_as_error()
    {
        if (!OperatingSystem.IsWindows()) return;

        var script = WriteScript("""
            @echo off
            more > nul
            echo {"type":"result","subtype":"success","is_error":true,"result":"API Error: 429 rate_limit_error","num_turns":1}
            """);

        var result = await Runner(script).RunAsync(new ClaudeCliRun("opus", "hi", _workspace.Root, [], TimeSpan.FromMinutes(1)), null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("429", result.Text);
    }

    [Fact]
    public async Task Exit_without_result_includes_stderr()
    {
        if (!OperatingSystem.IsWindows()) return;

        var script = WriteScript("""
            @echo off
            more > nul
            echo Not logged in 1>&2
            exit /b 3
            """);

        var ex = await Assert.ThrowsAsync<ClaudeCliException>(() =>
            Runner(script).RunAsync(new ClaudeCliRun("opus", "hi", _workspace.Root, [], TimeSpan.FromMinutes(1)), null, null, CancellationToken.None));

        Assert.Contains("exit 3", ex.Message);
        Assert.Contains("Not logged in", ex.Message);
    }

    [Fact]
    public async Task Usage_of_a_stopped_run_comes_from_its_assistant_events()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Two events of the same API call (partial then final output count), a second call, then the CLI keeps running
        // the way it does after submit_plan until the planner stops it.
        var script = WriteScript("""
            @echo off
            more > nul
            echo {"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"a"}],"usage":{"input_tokens":3,"cache_creation_input_tokens":1000,"output_tokens":1}}}
            echo {"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}],"usage":{"input_tokens":3,"cache_creation_input_tokens":1000,"output_tokens":40}}}
            echo {"type":"assistant","message":{"id":"m2","content":[{"type":"tool_use","id":"t2","name":"mcp__aithread__submit_plan","input":{}}],"usage":{"input_tokens":2,"cache_read_input_tokens":1000,"cache_creation_input_tokens":500,"output_tokens":200}}}
            ping -n 30 127.0.0.1 > nul
            """);
        using var stop = new CancellationTokenSource();
        var usage = new ClaudeCliUsage();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(script).RunAsync(
            new ClaudeCliRun("opus", "hi", _workspace.Root, [], TimeSpan.FromMinutes(1)),
            use =>
            {
                if (use.Name.EndsWith("submit_plan", StringComparison.Ordinal)) stop.Cancel();
                return Task.CompletedTask;
            },
            usage,
            stop.Token));

        Assert.Equal(2, usage.Calls);
        Assert.Equal(3 + 1000 + 40 + 2 + 1000 + 500 + 200, usage.Tokens);
    }

    [Fact]
    public async Task Missing_executable_explains_how_to_fix_it()
    {
        var ping = await Runner(Path.Combine(_workspace.Root, "missing", "claude.exe")).PingAsync("opus", CancellationToken.None);

        Assert.False(ping.Success);
        Assert.Contains("AiThread:ClaudeCli:Command", ping.Error);
    }

    [Fact]
    public void Mcp_tools_are_memory_thread_records_and_submit_plan_only()
    {
        var names = ClaudeCliPlanner.ToolDefinitions().Select(t => t!["name"]!.GetValue<string>()).ToList();

        Assert.Equal([PlannerTools.SearchMemory, PlannerTools.ReadMemory, PlannerTools.ReadThread, PlannerTools.SubmitPlan], names);
        Assert.All(ClaudeCliPlanner.ToolDefinitions(), t => Assert.Equal("object", t!["inputSchema"]!["type"]!.GetValue<string>()));
    }

    public void Dispose() => _workspace.Dispose();

    private string WriteScript(string content) =>
        _workspace.Write("cli with space/claude.cmd", content.ReplaceLineEndings("\r\n") + "\r\n");

    private ClaudeCliRunner Runner(string command) =>
        new(new StaticOptions(new AiThreadOptions { ClaudeCli = { Command = command } }), _registry, NullLogger<ClaudeCliRunner>.Instance);

    private sealed class StaticOptions(AiThreadOptions value) : IOptionsMonitor<AiThreadOptions>
    {
        public AiThreadOptions CurrentValue => value;

        public AiThreadOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AiThreadOptions, string?> listener) => null;
    }
}

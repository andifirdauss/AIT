using System.Text;
using System.Text.Json.Nodes;
using AIThread.API.Infrastructure;
using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Service.CliBackend;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// Runs the real Claude Code CLI of this machine (one cheap haiku request per test) to check the flags, the stream
// parsing and the written tool protocol end to end. Opt-in: set AIT_LIVE_CLAUDE=1; otherwise the tests pass without
// doing anything, so CI never spends anyone's account.
public class ClaudeCliLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AIT_LIVE_CLAUDE") == "1";

    private static CliModelService Service() =>
        new([new ClaudeCliAdapter(Options.Create(new AIThreadOptions()), NullLogger<ClaudeCliAdapter>.Instance)]);

    private static readonly CliCallLimits Limits = new(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2));

    [Fact]
    public async Task Plain_answer_streams_and_reports_usage()
    {
        if (!Enabled) return;
        var deltas = new List<string>();

        var result = await Service().ChatAsync(new CliModelId("claude", "haiku"),
            new ChatCompletionRequest { Model = "cli:claude:haiku", Messages = [ChatMessages.System("You are terse."), ChatMessages.User("Reply with exactly: pong")] },
            Limits, d => { deltas.Add(d); return Task.CompletedTask; });

        Assert.Contains("pong", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(deltas);
        Assert.True(result.PromptTokens > 0);
        Assert.True(result.CompletionTokens > 0);
    }

    // The whole pipeline on the CLI: planner (tools in text), executor (SEARCH/REPLACE applied by AIT), wrapper. The
    // change is written by AIT, so it is journaled like any 9router thread.
    [Fact]
    public async Task Thread_on_cli_nodes_edits_the_file_and_journals_it()
    {
        if (!Enabled) return;
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: services =>
            services.AddSingleton<INineRouterService>(sp => new NineRouterService(sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ISettingService>(), sp.GetRequiredService<ISecretGuard>(), NullLogger<NineRouterService>.Instance,
                cliModels: sp.GetRequiredService<ICliModelService>())));
        await harness.AddNodeAsync(NodeRole.Planner, "cli:claude:sonnet");
        await harness.AddNodeAsync(NodeRole.Executor, "cli:claude:haiku");
        await harness.AddNodeAsync(NodeRole.Wrapper, "cli:claude:haiku");
        harness.Project.Write("src/Greeter.cs", "namespace Demo;\n\npublic static class Greeter\n{\n    public static string Greet(string name) => $\"Hello, {name}!\";\n}\n");

        var observer = new RecordingObserver();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var result = await harness.Orchestrator.RunAsync(new ThreadRequest(ThreadSource.Dashboard,
            "In src/Greeter.cs change the greeting word from \"Hello\" to \"Halo\". Change nothing else.",
            null, null, harness.Project.Root, harness.ProjectId, null), observer, timeout.Token);

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("Halo, {name}!", harness.ReadFile("src/Greeter.cs"));
        await using var db = await harness.CreateDbAsync();
        Assert.True(await db.ThreadFileSnapshots.AnyAsync(s => s.ThreadRunId == result.ThreadId));
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.NotEmpty(thread.Tasks);
    }

    // A request the proxy does not orchestrate (a session title, here) with a cli: passthrough model: answered by the
    // CLI in the client's own format, plain JSON and SSE.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proxy_passthrough_on_a_cli_model_answers_in_the_client_format(bool stream)
    {
        if (!Enabled) return;
        await using var harness = await OrchestrationHarness.CreateAsync(
            settings => settings.PassthroughModel = "cli:claude:haiku",
            services => services.AddSingleton<INineRouterService>(sp => new NineRouterService(sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ISettingService>(), sp.GetRequiredService<ISecretGuard>(), NullLogger<NineRouterService>.Instance,
                cliModels: sp.GetRequiredService<ICliModelService>())));
        var handler = ActivatorUtilities.CreateInstance<AgentProxyHandler>(harness.Services);
        var body = new JsonObject
        {
            ["model"] = "claude-haiku-5-5",
            ["max_tokens"] = 64,
            ["stream"] = stream,
            ["system"] = "Write a 3-word title for the conversation. Reply with the title only.",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Please fix the login bug in the auth module." } }
        };
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString()));
        var output = new MemoryStream();
        http.Response.Body = output;

        await handler.HandleAsync(http, new ProxyCall(ClientProtocol.AnthropicMessages), CancellationToken.None);

        var text = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal(200, http.Response.StatusCode);
        if (stream)
        {
            Assert.Contains("event: content_block_delta", text);
            Assert.Contains("event: message_stop", text);
        }
        else
        {
            var reply = JsonNode.Parse(text)!;
            Assert.Equal("message", reply["type"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(reply["content"]![0]!["text"]!.GetValue<string>()));
        }
    }

    [Fact]
    public async Task Tool_protocol_round_trip_gives_a_call_then_an_answer()
    {
        if (!Enabled) return;
        var tools = new JsonArray
        {
            ChatMessages.Function("read_file", "Reads a file of the project and returns its text.", new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("path")
            })
        };
        var messages = new JsonArray
        {
            ChatMessages.System("You answer questions about a project. Read files with the tools before answering."),
            ChatMessages.User("What is the version written in the file VERSION.txt?")
        };
        var service = Service();

        var first = await service.ChatAsync(new CliModelId("claude", "haiku"),
            new ChatCompletionRequest { Model = "cli:claude:haiku", Messages = messages, Tools = tools }, Limits);

        var call = Assert.Single(first.ToolCalls);
        Assert.Equal("read_file", call.Name);
        Assert.Contains("VERSION.txt", JsonNode.Parse(call.Arguments)!["path"]!.GetValue<string>());

        messages.Add(ChatMessages.Assistant(first.Content, first.ToolCalls));
        messages.Add(ChatMessages.Tool(call.Id, "4.2.7"));
        var second = await service.ChatAsync(new CliModelId("claude", "haiku"),
            new ChatCompletionRequest { Model = "cli:claude:haiku", Messages = messages, Tools = tools }, Limits);

        Assert.Empty(second.ToolCalls);
        Assert.Contains("4.2.7", second.Content);
    }
}

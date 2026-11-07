using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Service.CliBackend;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// Runs the real Codex CLI / Gemini CLI / Antigravity CLI of this machine (a few cheap requests per test) to check the flags, the event
// parsing and the written tool protocol end to end. Opt-in per CLI: AIT_LIVE_CODEX=1, AIT_LIVE_GEMINI=1, AIT_LIVE_AGY=1 (with a
// model in AIT_LIVE_CODEX_MODEL / AIT_LIVE_GEMINI_MODEL / AIT_LIVE_AGY_MODEL, else the CLI default / flash /
// gemini-3.8-flash-low); otherwise the tests pass
// without doing anything, so CI never spends anyone's account.
public class CliEngineLiveTests
{
    private static readonly CliCallLimits Limits = new(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2));

    private static (CliModelService Service, CliModelId Model)? Engine(string tool)
    {
        var options = Options.Create(new AIThreadOptions());
        switch (tool)
        {
            case CliModelId.Codex when Environment.GetEnvironmentVariable("AIT_LIVE_CODEX") == "1":
                return (new CliModelService([new CodexCliAdapter(options, NullLogger<CodexCliAdapter>.Instance)]),
                    new CliModelId(tool, Environment.GetEnvironmentVariable("AIT_LIVE_CODEX_MODEL") ?? string.Empty));
            case CliModelId.Gemini when Environment.GetEnvironmentVariable("AIT_LIVE_GEMINI") == "1":
                return (new CliModelService([new GeminiCliAdapter(options, NullLogger<GeminiCliAdapter>.Instance)]),
                    new CliModelId(tool, Environment.GetEnvironmentVariable("AIT_LIVE_GEMINI_MODEL") ?? "flash"));
            case CliModelId.Antigravity when Environment.GetEnvironmentVariable("AIT_LIVE_AGY") == "1":
                return (new CliModelService([new AntigravityCliAdapter(options, NullLogger<AntigravityCliAdapter>.Instance)]),
                    new CliModelId(tool, Environment.GetEnvironmentVariable("AIT_LIVE_AGY_MODEL") ?? "gemini-3.8-flash-low"));
            default:
                return null;
        }
    }

    [Theory]
    [InlineData(CliModelId.Codex)]
    [InlineData(CliModelId.Gemini)]
    [InlineData(CliModelId.Antigravity)]
    public async Task Plain_answer_comes_back_with_usage(string tool)
    {
        if (Engine(tool) is not { } engine) return;
        var (service, model) = engine;
        var deltas = new List<string>();

        var result = await service.ChatAsync(model,
            new ChatCompletionRequest { Model = model.Id, Messages = [ChatMessages.System("You are terse."), ChatMessages.User("Reply with exactly: pong")] },
            Limits, d => { deltas.Add(d); return Task.CompletedTask; });

        Assert.Contains("pong", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(deltas);
        Assert.True(result.PromptTokens > 0);
    }

    [Theory]
    [InlineData(CliModelId.Codex)]
    [InlineData(CliModelId.Gemini)]
    [InlineData(CliModelId.Antigravity)]
    public async Task Tool_protocol_round_trip_gives_a_call_then_an_answer(string tool)
    {
        if (Engine(tool) is not { } engine) return;
        var (service, model) = engine;
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

        var first = await service.ChatAsync(model, new ChatCompletionRequest { Model = model.Id, Messages = messages, Tools = tools }, Limits);

        var call = Assert.Single(first.ToolCalls);
        Assert.Equal("read_file", call.Name);
        Assert.Contains("VERSION.txt", JsonNode.Parse(call.Arguments)!["path"]!.GetValue<string>());

        messages.Add(ChatMessages.Assistant(first.Content, first.ToolCalls));
        messages.Add(ChatMessages.Tool(call.Id, "4.2.7"));
        var second = await service.ChatAsync(model, new ChatCompletionRequest { Model = model.Id, Messages = messages, Tools = tools }, Limits);

        Assert.Empty(second.ToolCalls);
        Assert.Contains("4.2.7", second.Content);
    }

    [Theory]
    [InlineData(CliModelId.Codex)]
    [InlineData(CliModelId.Gemini)]
    [InlineData(CliModelId.Antigravity)]
    public async Task Detect_finds_the_installed_cli(string tool)
    {
        if (Engine(tool) is not { } engine) return;
        var service = engine.Service;

        var status = Assert.Single(await service.DetectAsync(refresh: true));

        Assert.True(status.Installed, status.Error);
        Assert.False(string.IsNullOrWhiteSpace(status.Version), status.Error);
    }

    // The whole pipeline on Antigravity CLI nodes: planner (tools in text), executor (SEARCH/REPLACE applied by AIT),
    // wrapper. The change is written by AIT, so it is journaled like any 9router thread.
    [Fact]
    public async Task Thread_on_antigravity_nodes_edits_the_file_and_journals_it()
    {
        if (Engine(CliModelId.Antigravity) is not { } engine) return;
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: services =>
            services.AddSingleton<INineRouterService>(sp => new NineRouterService(sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ISettingService>(), sp.GetRequiredService<ISecretGuard>(), NullLogger<NineRouterService>.Instance,
                cliModels: engine.Service)));
        var model = engine.Model.Id;
        await harness.AddNodeAsync(NodeRole.Planner, model);
        await harness.AddNodeAsync(NodeRole.Executor, model);
        await harness.AddNodeAsync(NodeRole.Wrapper, model);
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
    }

    // `agy models` of the signed-in account feeds the Node and Setup pickers.
    [Fact]
    public async Task Antigravity_models_are_listed_from_the_account()
    {
        if (Engine(CliModelId.Antigravity) is not { } engine) return;

        var models = await engine.Service.GetModelsAsync();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.StartsWith("cli:agy", m.Id));
        Assert.All(models, m => Assert.Equal("Antigravity (CLI)", m.OwnedBy));
    }
}

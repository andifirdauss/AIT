using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.CliBackend;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Tests;

// Codex and Gemini CLI as model engines, and the engine choice of CliModelService. The CLIs themselves are not run here
// (see the opt-in live tests); these check the event parsing, the arguments, the loop guards and the error mapping.
public class CliEngineTests
{
    [Theory]
    [InlineData("cli:codex", "codex", "")]
    [InlineData("cli:codex:gpt-5.5", "codex", "gpt-5.5")]
    [InlineData("cli:gemini:flash-lite", "gemini", "flash-lite")]
    public void Model_id_names_codex_and_gemini(string id, string tool, string model)
    {
        Assert.True(CliModelId.TryParse(id, out var parsed));
        Assert.Equal(tool, parsed.Tool);
        Assert.Equal(model, parsed.Model);
    }

    [Fact]
    public void Codex_reader_takes_the_last_agent_message_and_the_usage()
    {
        var reader = new CodexStreamReader();
        foreach (var line in new[]
                 {
                     """{"type":"thread.started","thread_id":"0199a213-81c0-7800-8aa1-bbab2a035a53"}""",
                     """{"type":"turn.started"}""",
                     "a warning that is not json",
                     """{"type":"item.completed","item":{"id":"item_0","type":"reasoning","text":"**Thinking**"}}""",
                     """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"Let me look."}}""",
                     """{"type":"item.completed","item":{"id":"item_2","type":"command_execution","command":"ls","status":"failed"}}""",
                     """{"type":"item.completed","item":{"id":"item_3","type":"agent_message","text":"pong"}}""",
                     """{"type":"turn.completed","usage":{"input_tokens":1200,"cached_input_tokens":1000,"output_tokens":5}}"""
                 })
            reader.Feed(line);

        Assert.True(reader.Completed);
        Assert.Null(reader.FailedMessage);
        Assert.Equal("pong", reader.Text);
        Assert.Equal(1, reader.CommandCount);
        Assert.Equal(1200, reader.InputTokens);
        Assert.Equal(1000, reader.CachedInputTokens);
        Assert.Equal(5, reader.OutputTokens);
    }

    [Fact]
    public void Codex_reader_reports_a_failed_turn_with_its_status()
    {
        var reader = new CodexStreamReader();
        reader.Feed("""{"type":"error","message":"Reconnecting... 1/5"}""");
        reader.Feed("""{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer"}}""");

        Assert.False(reader.Completed);
        Assert.Equal(401, reader.ApiStatus);

        using var _ = Lang.Use("en");
        var failure = CodexCliAdapter.Failure("", reader.ApiStatus, reader.FailedMessage!);
        Assert.True(failure.IsProviderLoginRequired);
        Assert.Contains("codex login", failure.Message);
    }

    [Fact]
    public void Codex_arguments_read_stdin_last_and_put_images_first()
    {
        var arguments = CodexCliAdapter.BuildArguments("gpt-5.5", @"C:\work\instructions.md", [@"C:\work\image-1.png"], ignoreUserConfig: true);

        Assert.Equal("exec", arguments[0]);
        Assert.Equal(["--image", @"C:\work\image-1.png"], arguments.Skip(1).Take(2));
        Assert.Equal("-", arguments[^1]);
        Assert.Contains("--ephemeral", arguments);
        Assert.Contains("--json", arguments);
        Assert.Contains("--ignore-user-config", arguments);
        Assert.Contains(@"model_instructions_file='C:\work\instructions.md'", arguments);
        var sandbox = arguments.ToList().IndexOf("--sandbox");
        Assert.Equal("read-only", arguments[sandbox + 1]);
        var model = arguments.ToList().IndexOf("--model");
        Assert.Equal("gpt-5.5", arguments[model + 1]);

        var defaults = CodexCliAdapter.BuildArguments("", "i.md", [], ignoreUserConfig: false);
        Assert.DoesNotContain("--model", defaults);
        Assert.DoesNotContain("--ignore-user-config", defaults);
        Assert.DoesNotContain("--image", defaults);
    }

    [Fact]
    public void Codex_config_routing_to_ait_is_detected_from_the_top_level_keys_only()
    {
        Assert.True(CodexCliAdapter.RoutesToAit("model = \"gpt-5.5\"\nprofile = \"aithread\"\n"));
        Assert.True(CodexCliAdapter.RoutesToAit("model_provider = 'aithread'\n[model_providers.aithread]\nname = \"AIT\"\n"));
        // The menu's own provider and profile tables do not route plain codex through AIT.
        Assert.False(CodexCliAdapter.RoutesToAit("model = \"gpt-5.5\"\n[profiles.aithread]\nmodel_provider = \"aithread\"\n"));
        Assert.False(CodexCliAdapter.RoutesToAit(""));
    }

    [Fact]
    public void Codex_environment_drops_a_loopback_base_url_only()
    {
        var loop = new Dictionary<string, string?> { ["OPENAI_BASE_URL"] = "http://127.0.0.1:5080/v1", ["OPENAI_API_KEY"] = "aithread-local" };
        CodexCliAdapter.CleanEnvironment(loop);
        Assert.Empty(loop);

        var gateway = new Dictionary<string, string?> { ["OPENAI_BASE_URL"] = "https://llm.company.example/v1", ["OPENAI_API_KEY"] = "k" };
        CodexCliAdapter.CleanEnvironment(gateway);
        Assert.Equal(2, gateway.Count);
    }

    [Fact]
    public void Gemini_reader_streams_assistant_deltas_and_reads_the_stats()
    {
        var reader = new GeminiStreamReader();
        var deltas = new List<string>();
        foreach (var line in new[]
                 {
                     """{"type":"init","timestamp":"2026-11-07T10:00:00.000Z","session_id":"abc","model":"gemini-pro"}""",
                     """{"type":"message","role":"user","content":"Reply with exactly: pong"}""",
                     """{"type":"message","role":"assistant","content":"po","delta":true}""",
                     """{"type":"message","role":"assistant","content":"ng","delta":true}""",
                     """{"type":"result","status":"success","stats":{"total_tokens":120,"input_tokens":110,"output_tokens":10,"cached":40,"input":70,"duration_ms":900,"tool_calls":0}}"""
                 })
        {
            if (reader.Feed(line) is { } delta) deltas.Add(delta);
        }

        Assert.Equal(["po", "ng"], deltas);
        Assert.True(reader.SawResult);
        Assert.False(reader.IsError);
        Assert.Equal("pong", reader.Text);
        Assert.Equal(110, reader.InputTokens);
        Assert.Equal(40, reader.CachedTokens);
        Assert.Equal(10, reader.OutputTokens);
    }

    [Fact]
    public void Gemini_reader_reports_an_error_result()
    {
        var reader = new GeminiStreamReader();
        reader.Feed("""{"type":"result","status":"error","error":{"type":"FatalAuthenticationError","message":"Please set an Auth method"}}""");

        Assert.True(reader.IsError);
        Assert.Equal("FatalAuthenticationError", reader.ErrorType);

        using var _ = Lang.Use("en");
        var failure = GeminiCliAdapter.Failure("pro", null, reader.ErrorMessage!);
        Assert.True(failure.IsProviderLoginRequired);
        Assert.Contains("/auth", failure.Message);

        var limited = GeminiCliAdapter.Failure("pro", CliFailure.StatusIn("got status: 429 Too Many Requests"), "RESOURCE_EXHAUSTED");
        Assert.True(limited.IsRateLimited);
    }

    [Fact]
    public void Gemini_workspace_settings_deny_every_tool_and_mcp_server()
    {
        var settings = System.Text.Json.Nodes.JsonNode.Parse(GeminiCliAdapter.WorkspaceSettings())!;

        Assert.Empty(settings["tools"]!["core"]!.AsArray());
        Assert.Contains(settings["tools"]!["exclude"]!.AsArray(), t => t!.GetValue<string>() == "run_shell_command");
        Assert.Empty(settings["mcpServers"]!.AsObject());

        var arguments = GeminiCliAdapter.BuildArguments("flash");
        Assert.Equal(["--output-format", "stream-json", "--extensions", "none", "--allowed-mcp-server-names", "none", "--model", "flash"], arguments);
    }

    [Fact]
    public void Gemini_user_text_guards_slash_commands_and_names_images()
    {
        Assert.Equal("Fix it.", GeminiCliAdapter.UserText(new CliPrompt("s", "Fix it.", [])));
        Assert.StartsWith("User message:\n/compact", GeminiCliAdapter.UserText(new CliPrompt("s", "/compact", [])));
        Assert.Contains("1 image(s)", GeminiCliAdapter.UserText(new CliPrompt("s", "What is this?", [new CliImage("image/png", "AAA=")])));
    }

    [Fact]
    public void Gemini_env_file_pointing_at_ait_sends_the_cli_to_google_instead()
    {
        var root = Path.Combine(Path.GetTempPath(), "ait-gemini-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var work = Path.Combine(home, "AppData", "Local", "AIThread", "cli-work", "x");
        try
        {
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(Path.Combine(home, ".gemini"));
            File.WriteAllText(Path.Combine(home, ".gemini", ".env"), "# >>> AIThread\nGOOGLE_GEMINI_BASE_URL=\"http://localhost:5080\"\nGEMINI_API_KEY=\"aithread-local\"\n");

            Assert.Equal(Path.Combine(home, ".gemini", ".env"), GeminiCliAdapter.FindEnvFile(work, home));
            var environment = new Dictionary<string, string?>();
            GeminiCliAdapter.CleanEnvironment(environment, work, home);
            Assert.Equal(GeminiCliAdapter.OfficialBaseUrl, environment["GOOGLE_GEMINI_BASE_URL"]);

            // A .env of the user's own gateway is left to the CLI.
            File.WriteAllText(Path.Combine(home, ".gemini", ".env"), "GOOGLE_GEMINI_BASE_URL=https://llm.company.example\n");
            var own = new Dictionary<string, string?>();
            GeminiCliAdapter.CleanEnvironment(own, work, home);
            Assert.Empty(own);

            // A loopback base URL in the environment itself.
            var loop = new Dictionary<string, string?> { ["GOOGLE_GEMINI_BASE_URL"] = "http://127.0.0.1:5080", ["GEMINI_API_KEY"] = "aithread-local" };
            GeminiCliAdapter.CleanEnvironment(loop, work, home);
            Assert.Equal(GeminiCliAdapter.OfficialBaseUrl, loop["GOOGLE_GEMINI_BASE_URL"]);
            Assert.False(loop.ContainsKey("GEMINI_API_KEY"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Lines as `agy` 1.5.2 printed them for "Reply with exactly: pong" (init tool list shortened).
    [Fact]
    public void Antigravity_reader_streams_agent_deltas_and_reads_the_result()
    {
        var reader = new AntigravityStreamReader();
        var deltas = new List<string>();
        foreach (var line in new[]
                 {
                     """{"event":"init","conversation_id":"4820f87f","init":{"model":"gemini-3.8-flash-low","cwd":"C:\\work","tools":["run_command"],"permission_mode":"request-review"}}""",
                     """{"event":"step_update","step_update":{"conversation_id":"4820f87f","step_index":0,"state":"DONE","step_type":"user_input"}}""",
                     """{"event":"step_update","step_update":{"conversation_id":"4820f87f","step_index":1,"state":"ACTIVE","step_type":"agent_response","text_delta":"pong"}}""",
                     """{"event":"step_update","step_update":{"conversation_id":"4820f87f","step_index":1,"state":"DONE","step_type":"agent_response","text_delta":"\n","duration_seconds":2.43,"usage":{"input_tokens":6952,"output_tokens":1,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":6953}}}""",
                     """{"event":"result","result":{"conversation_id":"4820f87f","status":"SUCCESS","response":"pong\n","duration_seconds":2.49,"num_turns":1,"usage":{"input_tokens":6952,"output_tokens":1,"thinking_tokens":0,"cache_read_tokens":100,"total_tokens":6953}}}"""
                 })
        {
            if (reader.Feed(line) is { } delta) deltas.Add(delta);
        }

        Assert.Equal(["pong", "\n"], deltas);
        Assert.True(reader.SawResult);
        Assert.False(reader.IsError);
        Assert.Equal("pong\n", reader.Text);
        Assert.Equal(6952, reader.InputTokens);
        Assert.Equal(1, reader.OutputTokens);
        Assert.Equal(100, reader.CacheReadTokens);
        Assert.Equal(0, reader.ToolUseCount);
    }

    [Fact]
    public void Antigravity_reader_counts_tool_steps_and_reports_an_error_result()
    {
        var reader = new AntigravityStreamReader();
        reader.Feed("""{"event":"step_update","step_update":{"step_index":2,"state":"ACTIVE","step_type":"tool","tool_name":"run_command"}}""");
        reader.Feed("""{"event":"step_update","step_update":{"step_index":2,"state":"ERROR","step_type":"tool","tool_name":"run_command"}}""");
        reader.Feed("""{"event":"result","result":{"status":"ERROR","response":"","error":"failed to decode stream input","finished_reason":"error"}}""");

        Assert.Equal(1, reader.ToolUseCount);
        Assert.True(reader.IsError);
        Assert.Equal("failed to decode stream input", reader.Error);
    }

    [Fact]
    public void Antigravity_forgets_only_the_conversation_of_its_own_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ait-agy-" + Guid.NewGuid().ToString("N"));
        var mine = Guid.NewGuid().ToString("D");
        var other = Guid.NewGuid().ToString("D");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "conversations"));
            foreach (var id in new[] { mine, other })
            {
                File.WriteAllText(Path.Combine(root, "conversations", id + ".db"), "x");
                Directory.CreateDirectory(Path.Combine(root, "brain", id, ".system_generated"));
            }
            File.WriteAllText(Path.Combine(root, "conversations", mine + ".db-wal"), "x");
            File.WriteAllText(Path.Combine(root, "conversation_summaries.db"), "index");

            var reader = new AntigravityStreamReader();
            reader.Feed("{\"event\":\"init\",\"conversation_id\":\"" + mine + "\",\"init\":{\"model\":\"m\"}}");
            Assert.Equal(mine, reader.ConversationId);

            Assert.True(AntigravityCliAdapter.ForgetConversation(reader.ConversationId, root));
            Assert.False(File.Exists(Path.Combine(root, "conversations", mine + ".db")));
            Assert.False(File.Exists(Path.Combine(root, "conversations", mine + ".db-wal")));
            Assert.False(Directory.Exists(Path.Combine(root, "brain", mine)));
            Assert.True(File.Exists(Path.Combine(root, "conversations", other + ".db")));
            Assert.True(Directory.Exists(Path.Combine(root, "brain", other)));
            Assert.True(File.Exists(Path.Combine(root, "conversation_summaries.db")));

            // Anything but a GUID names nothing.
            Assert.False(AntigravityCliAdapter.ForgetConversation("..\\conversations", root));
            Assert.False(AntigravityCliAdapter.ForgetConversation(null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Antigravity_models_come_from_the_listing()
    {
        var models = AntigravityCliAdapter.ParseModels(
        [
            "Fetching available models...",
            "gemini-3.8-flash-high\tGemini 3.8 Flash (High)",
            "claude-opus-4-6-thinking\tClaude Opus 4.6 (Thinking)",
            "gpt-oss-120b-medium\tGPT-OSS 120B (Medium)",
            ""
        ]);

        Assert.Equal(["gemini-3.8-flash-high", "claude-opus-4-6-thinking", "gpt-oss-120b-medium"], models);
    }

    [Fact]
    public void Antigravity_runs_as_a_workspace_agent_without_tools_and_with_the_ait_prompt()
    {
        var agent = AntigravityCliAdapter.AgentFile("You plan.\nAlways answer in JSON.").Replace("\r\n", "\n");
        Assert.StartsWith("---\nname: ait\n", agent);
        Assert.Contains("\ntools: []\n", agent);
        Assert.Contains("\ncommandExecutionPolicy: off\n", agent);
        Assert.Contains("---\nYou cannot call functions or tools natively", agent);
        Assert.EndsWith("\n\nYou plan.\nAlways answer in JSON.\n", agent);
        Assert.True(AntigravityCliAdapter.IsMalformedFunctionCall(
            "Your previous response contained an improperly formatted function call: Malformed function call: Failed to parse function call"));
        Assert.False(AntigravityCliAdapter.IsMalformedFunctionCall("rate limit reached"));

        var deny = System.Text.Json.Nodes.JsonNode.Parse(AntigravityCliAdapter.PermissionSettings())!["permissions"]!["deny"]!.AsArray()
            .Select(r => r!.GetValue<string>()).ToList();
        Assert.Contains("command(*)", deny);
        Assert.Contains("read_file(*)", deny);
        Assert.Contains("write_file(*)", deny);

        var arguments = AntigravityCliAdapter.BuildArguments("gemini-3.8-flash-low", @"C:\work\ait-settings.json");
        Assert.Equal(["--input-format", "stream-json", "--output-format", "stream-json", "--disable-slash-commands", "--agent", "ait",
            "--settings", @"C:\work\ait-settings.json", "--model", "gemini-3.8-flash-low"], arguments);

        var line = AntigravityCliAdapter.InputLine(new CliPrompt("s", "Look\nhere", [new CliImage("image/png", "AAA=")]));
        Assert.DoesNotContain('\n', line);
        var json = System.Text.Json.Nodes.JsonNode.Parse(line)!;
        Assert.Equal("user", json["event"]!.GetValue<string>());
        Assert.StartsWith("Look\nhere", json["message"]!["content"]!.GetValue<string>());
        Assert.Contains("1 image(s)", json["message"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Model_service_runs_the_engine_of_the_tool_and_lists_installed_models()
    {
        var claude = new FakeEngine("claude", "Claude Code", ["opus", "haiku"], installed: true);
        var codex = new FakeEngine("codex", "Codex", [""], installed: true);
        var gemini = new FakeEngine("gemini", "Gemini", ["pro"], installed: false);
        var service = new CliModelService([claude, codex, gemini]);

        var result = await service.ChatAsync(new CliModelId("codex", "gpt-5.5"),
            new ChatCompletionRequest { Model = "cli:codex:gpt-5.5", Messages = [ChatMessages.User("hi")] }, new CliCallLimits(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));

        Assert.Equal("codex:gpt-5.5", result.Content);
        Assert.Equal(0, claude.Calls);
        Assert.Equal(1, codex.Calls);

        var models = await service.GetModelsAsync();
        Assert.Equal(["cli:claude:opus", "cli:claude:haiku", "cli:codex"], models.Select(m => m.Id));
        Assert.Equal("Codex (CLI)", models[^1].OwnedBy);

        await Assert.ThrowsAsync<NineRouterException>(() => service.ChatAsync(new CliModelId("qwen", ""),
            new ChatCompletionRequest { Model = "cli:qwen", Messages = [ChatMessages.User("hi")] }, new CliCallLimits(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))));
    }

    private sealed class FakeEngine(string tool, string name, IReadOnlyList<string> models, bool installed) : ICliEngine
    {
        public int Calls { get; private set; }

        public string Tool => tool;

        public string Name => name;

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(models);

        public Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new CliRunOutput($"{tool}:{model}", "stop", 1, 1, 0));
        }

        public Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CliBackendStatusDto(tool, name, installed, installed ? tool : null, installed ? "1.0" : null, null));
    }
}

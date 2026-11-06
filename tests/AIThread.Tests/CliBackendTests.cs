using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.CliBackend;
using AIThread.Service.Models;

namespace AIThread.Tests;

public class CliBackendTests
{
    [Theory]
    [InlineData("cli:claude:opus", "claude", "opus")]
    [InlineData("CLI:Claude:claude-sonnet-5", "claude", "claude-sonnet-5")]
    [InlineData("cli:claude", "claude", "")]
    public void CliModelId_parses_tool_and_model(string id, string tool, string model)
    {
        Assert.True(CliModelId.TryParse(id, out var parsed));
        Assert.Equal(tool, parsed.Tool);
        Assert.Equal(model, parsed.Model);
    }

    [Theory]
    [InlineData("cc/claude-opus-5-5")]
    [InlineData("cli:")]
    [InlineData(null)]
    public void CliModelId_rejects_router_models(string? id) => Assert.False(CliModelId.TryParse(id, out _));

    [Fact]
    public void Transcript_of_a_single_user_turn_is_the_turn_itself()
    {
        var prompt = CliTranscript.Render([ChatMessages.System("You review code."), ChatMessages.User("Review this.")], null);

        Assert.Equal("You review code.", prompt.System);
        Assert.Equal("Review this.", prompt.User);
        Assert.Empty(prompt.Images);
    }

    [Fact]
    public void Transcript_replays_tool_calls_and_results_and_documents_the_tools()
    {
        var tools = new JsonArray
        {
            ChatMessages.Function("read_file", "Reads a file.", new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" } }
            })
        };
        var messages = new JsonArray
        {
            ChatMessages.System("You plan."),
            ChatMessages.User("Fix the bug."),
            ChatMessages.Assistant("Looking.", [new ToolCall("call_1", "read_file", "{\"path\":\"a.cs\"}")]),
            ChatMessages.Tool("call_1", "class A {}")
        };

        var prompt = CliTranscript.Render(messages, tools);

        Assert.Contains("### read_file", prompt.System);
        Assert.Contains("<tool_call name=\"TOOL_NAME\">", prompt.System);
        Assert.Contains("<tool_call name=\"read_file\">{\"path\":\"a.cs\"}</tool_call>", prompt.User);
        Assert.Contains("<tool_result name=\"read_file\">", prompt.User);
        Assert.Contains("class A {}", prompt.User);
        Assert.Contains("<user>\nFix the bug.", prompt.User.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Transcript_takes_the_images_of_the_last_user_turn()
    {
        var prompt = CliTranscript.Render([ChatMessages.User("What is this?", [new ImageAttachment("image/png", "iVBORw0KGgo=")])], null);

        var image = Assert.Single(prompt.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("iVBORw0KGgo=", image.Data);
        Assert.Equal("What is this?", prompt.User);
    }

    [Fact]
    public void Parser_reads_several_calls_and_keeps_the_text()
    {
        var parsed = CliToolCallParser.Parse("""
            I will read both files.
            <tool_call name="read_file">{"path":"a.cs"}</tool_call>
            <tool_call name="read_file">
            ```json
            {"path": "b.cs"}
            ```
            </tool_call>
            """);

        Assert.Equal("I will read both files.", parsed.Content);
        Assert.Equal(2, parsed.ToolCalls.Count);
        Assert.All(parsed.ToolCalls, c => Assert.Equal("read_file", c.Name));
        Assert.Equal("{\"path\":\"a.cs\"}", parsed.ToolCalls[0].Arguments);
        Assert.Equal("{\"path\":\"b.cs\"}", parsed.ToolCalls[1].Arguments);
        Assert.NotEqual(parsed.ToolCalls[0].Id, parsed.ToolCalls[1].Id);
    }

    [Fact]
    public void Parser_ignores_examples_in_code_fences_and_invalid_json()
    {
        const string reply = """
            Use it like this:
            ```
            <tool_call name="read_file">{"path":"x"}</tool_call>
            ```
            <tool_call name="search_text">not json</tool_call>
            """;

        var parsed = CliToolCallParser.Parse(reply);

        Assert.Empty(parsed.ToolCalls);
        Assert.Equal(reply, parsed.Content);
    }

    [Fact]
    public void Stream_reader_collects_deltas_usage_and_stop_reason()
    {
        var reader = new ClaudeStreamReader();
        var deltas = new List<string>();
        foreach (var line in new[]
                 {
                     """{"type":"system","subtype":"init","model":"claude-haiku-5-5"}""",
                     "not json at all",
                     """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"po"}}}""",
                     """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ng"}}}""",
                     """{"type":"assistant","message":{"content":[{"type":"text","text":"pong"}]}}""",
                     """{"type":"stream_event","event":{"type":"message_delta","delta":{"stop_reason":"end_turn"}}}""",
                     """{"duration_api_ms":2162,"type":"result","subtype":"success","is_error":false,"result":"pong","usage":{"input_tokens":2,"cache_creation_input_tokens":938,"cache_read_input_tokens":10,"output_tokens":4}}"""
                 })
        {
            if (reader.Feed(line) is { } delta) deltas.Add(delta);
        }

        Assert.Equal(["po", "ng"], deltas);
        Assert.True(reader.SawResult);
        Assert.False(reader.IsError);
        Assert.Equal("pong", reader.Text);
        Assert.Equal("stop", reader.FinishReason);
        Assert.Equal(2, reader.InputTokens);
        Assert.Equal(938, reader.CacheCreationTokens);
        Assert.Equal(10, reader.CacheReadTokens);
        Assert.Equal(4, reader.OutputTokens);
    }

    [Fact]
    public void Stream_reader_reports_an_api_error()
    {
        var reader = new ClaudeStreamReader();
        reader.Feed("""{"type":"result","subtype":"success","is_error":true,"result":"API Error: 429 rate limited","api_error_status":429}""");

        Assert.True(reader.IsError);
        Assert.Equal(429, reader.ApiErrorStatus);
    }

    [Fact]
    public void Failures_map_to_router_errors_for_retry_and_fallback()
    {
        using var _ = Lang.Use("en");

        var login = ClaudeCliAdapter.Failure("opus", null, "success", "Invalid API key · Please run /login");
        Assert.True(login.IsProviderLoginRequired);
        Assert.True(login.IsModelUnavailable);
        Assert.False(login.IsTransient);
        Assert.Contains("not logged in", login.Message);

        var limited = ClaudeCliAdapter.Failure("opus", 429, "success", "rate limited");
        Assert.True(limited.IsRateLimited);

        var busy = ClaudeCliAdapter.Failure("opus", null, "success", "Overloaded");
        Assert.True(busy.IsTransient);

        var tools = ClaudeCliAdapter.Failure("opus", null, "error_max_turns", "max turns");
        Assert.Contains("built-in CLI tool", tools.Message);
    }

    [Fact]
    public void Environment_drops_a_loopback_base_url_but_keeps_a_company_gateway()
    {
        var loop = new Dictionary<string, string?>
        {
            ["ANTHROPIC_BASE_URL"] = "http://localhost:5080",
            ["ANTHROPIC_AUTH_TOKEN"] = "aithread-local",
            ["CLAUDECODE"] = "1",
            ["PATH"] = "x"
        };
        ClaudeCliAdapter.CleanEnvironment(loop);
        Assert.False(loop.ContainsKey("ANTHROPIC_BASE_URL"));
        Assert.False(loop.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.False(loop.ContainsKey("CLAUDECODE"));
        Assert.Equal("x", loop["PATH"]);
        Assert.Equal("1", loop["DISABLE_AUTOUPDATER"]);

        var gateway = new Dictionary<string, string?> { ["ANTHROPIC_BASE_URL"] = "https://llm.company.example" };
        ClaudeCliAdapter.CleanEnvironment(gateway);
        Assert.Equal("https://llm.company.example", gateway["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void Input_line_is_one_stream_json_user_message_with_images()
    {
        var line = ClaudeCliAdapter.InputLine(new CliPrompt("sys", "Look\nhere", [new CliImage("image/png", "AAA=")]));

        Assert.DoesNotContain('\n', line);
        var json = JsonNode.Parse(line)!;
        Assert.Equal("user", json["type"]!.GetValue<string>());
        var content = json["message"]!["content"]!.AsArray();
        Assert.Equal("Look\nhere", content[0]!["text"]!.GetValue<string>());
        Assert.Equal("image/png", content[1]!["source"]!["media_type"]!.GetValue<string>());
    }
}

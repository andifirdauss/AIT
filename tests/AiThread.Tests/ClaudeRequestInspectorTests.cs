using System.Text.Json.Nodes;
using AiThread.Service.Proxy;

namespace AiThread.Tests;

public class ClaudeRequestInspectorTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoHeaders = new Dictionary<string, string?>();

    private static JsonNode MainAgentRequest(JsonArray messages) => new JsonObject
    {
        ["model"] = "claude-opus-5-5",
        ["stream"] = true,
        ["metadata"] = new JsonObject { ["user_id"] = "user_x_account_y_session_0b7a1c52-8a5e-4f3c-9d7e-1a2b3c4d5e6f" },
        ["system"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = "You are Claude Code, Anthropic's official CLI for Claude." },
            new JsonObject { ["type"] = "text", ["text"] = "# Environment\n - Primary working directory: D:\\Source\\MyApp\n - Platform: win32" }
        },
        ["tools"] = new JsonArray { new JsonObject { ["name"] = "Read" } },
        ["messages"] = messages
    };

    [Fact]
    public void Main_agent_turn_is_orchestrated()
    {
        var body = MainAgentRequest(
        [
            new JsonObject { ["role"] = "user", ["content"] = "earlier question" },
            new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "earlier answer" } } },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "<system-reminder>CLAUDE.md says hi</system-reminder>" },
                    new JsonObject { ["type"] = "text", ["text"] = "Add a description column" }
                }
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 1000);

        Assert.True(info.IsOrchestratable);
        Assert.True(info.Stream);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
        Assert.Equal("0b7a1c52-8a5e-4f3c-9d7e-1a2b3c4d5e6f", info.SessionId);
        Assert.Equal("Add a description column", info.Prompt);
        Assert.Contains("CLAUDE.md", info.PromptContext);
        Assert.Contains("earlier answer", info.ConversationContext);
    }

    [Fact]
    public void Request_without_tools_is_passed_through()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-haiku-4-5",
            ["system"] = "Generate a title",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "hello" } }
        };

        Assert.False(ClaudeRequestInspector.Inspect(body, NoHeaders, 100).IsOrchestratable);
    }

    [Fact]
    public void Tool_result_turn_is_passed_through()
    {
        var body = MainAgentRequest(
        [
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray { new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "t1", ["content"] = "ok" } }
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.False(info.IsOrchestratable);
        Assert.Equal("tool_result turn", info.Reason);
    }

    [Fact]
    public void Headers_override_detection()
    {
        var body = MainAgentRequest([new JsonObject { ["role"] = "user", ["content"] = "go" }]);
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeRequestInspector.WorkingDirectoryHeader] = @"E:\Other",
            [ClaudeRequestInspector.SolutionHeader] = @"E:\Other\Other.slnx"
        };

        var info = ClaudeRequestInspector.Inspect(body, headers, 100);

        Assert.Equal(@"E:\Other", info.WorkingDirectory);
        Assert.Equal(@"E:\Other\Other.slnx", info.SolutionHint);
    }

    [Fact]
    public void Working_directory_with_was_suffix_is_cleaned()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-opus-5-5",
            ["stream"] = true,
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "Read" } },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = " - Primary working directory: D:\\Source\\MyApp (was d:\\source\\myapp)\nDo something"
                }
            }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
    }

    [Fact]
    public void Working_directory_detected_from_claude_md()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-opus-5-5",
            ["stream"] = true,
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "Read" } },
            ["system"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = "<system-reminder>Contents of D:\\Source\\MyOtherApp\\CLAUDE.md (project instructions)</system-reminder>" }
            },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "Do something" }
            }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal(@"D:\Source\MyOtherApp", info.WorkingDirectory);
    }

    [Fact]
    public void Assistant_prefill_turn_is_orchestrated()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-sonnet-5",
            ["stream"] = true,
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "Read" } },
            ["system"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "You are Claude Code" } },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "Buatkan UI Index untuk Master" },
                new JsonObject { ["role"] = "assistant", ["content"] = "" }
            }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("Buatkan UI Index untuk Master", info.Prompt);
    }

    [Fact]
    public void System_reminder_after_user_turn_is_orchestrated()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-sonnet-5",
            ["stream"] = true,
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "Read" } },
            ["system"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "You are Claude Code" } },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "Buatkan UI Index untuk Master" },
                new JsonObject { ["role"] = "system", ["content"] = "<system-reminder>Some reminder</system-reminder>" }
            }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("Buatkan UI Index untuk Master", info.Prompt);
        Assert.Contains("Some reminder", info.PromptContext);
    }
}

using System.Text.Json.Nodes;
using AIThread.Service.Orchestration;
using AIThread.Service.Proxy;

namespace AIThread.Tests;

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
    public void Message_typed_while_the_previous_turn_ran_is_the_prompt_without_its_notice()
    {
        var body = MainAgentRequest(
        [
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "<system-notice>\nUser interjection during work: priority; supersedes conflicting prior instructions.\n</system-notice>\nulangi"
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 1000);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("ulangi", info.Prompt);
        Assert.True(RetryReply.IsMatch(info.Prompt));
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

    [Theory]
    [InlineData("<title-request> Session UI request, not from the user. Your main reply continues separately; NEVER start on the task here.")]
    [InlineData("<recap> User stepped away; returning. Recap: <40 words, 1–2 plain sentences, no markdown.")]
    public void Claude_code_side_requests_are_passed_through(string prompt)
    {
        var body = MainAgentRequest(
        [
            new JsonObject { ["role"] = "user", ["content"] = "Add a description column" },
            new JsonObject { ["role"] = "assistant", ["content"] = "Done." },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "<system-reminder>CLAUDE.md says hi</system-reminder>" },
                    new JsonObject { ["type"] = "text", ["text"] = prompt }
                }
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.False(info.IsOrchestratable);
        Assert.StartsWith("Claude Code side request", info.Reason);
    }

    [Fact]
    public void Text_document_attached_to_the_turn_is_part_of_the_prompt()
    {
        var body = MainAgentRequest(
        [
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "Bisa kamu kerjakan ini?" },
                    new JsonObject
                    {
                        ["type"] = "document",
                        ["title"] = "paste-1.md",
                        ["source"] = new JsonObject { ["type"] = "text", ["media_type"] = "text/plain", ["data"] = "# EmployeeAPI\nSync JoinDate." }
                    },
                    new JsonObject
                    {
                        ["type"] = "document",
                        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "application/pdf", ["data"] = "JVBERi0=" }
                    }
                }
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("Bisa kamu kerjakan ini?\n<document title=\"paste-1.md\">\n# EmployeeAPI\nSync JoinDate.\n</document>", info.Prompt);
    }

    [Fact]
    public void Prompt_mentioning_a_recap_tag_later_is_still_orchestrated()
    {
        var body = MainAgentRequest([new JsonObject { ["role"] = "user", ["content"] = "Why does Claude Code send <recap> requests?" }]);

        Assert.True(ClaudeRequestInspector.Inspect(body, NoHeaders, 100).IsOrchestratable);
    }

    [Fact]
    public void Headers_override_detection()
    {
        var body = MainAgentRequest([new JsonObject { ["role"] = "user", ["content"] = "go" }]);
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeRequestInspector.WorkingDirectoryHeader] = @"E:\Other",
            [ClaudeRequestInspector.ProjectHeader] = @"E:\Other"
        };

        var info = ClaudeRequestInspector.Inspect(body, headers, 100);

        Assert.Equal(@"E:\Other", info.WorkingDirectory);
        Assert.Equal(@"E:\Other", info.ProjectHint);
    }

    [Fact]
    public void Legacy_solution_header_is_still_read_as_project_hint()
    {
        var body = MainAgentRequest([new JsonObject { ["role"] = "user", ["content"] = "go" }]);
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ClaudeRequestInspector.LegacySolutionHeader] = @"E:\Other\Other.slnx"
        };

        Assert.Equal(@"E:\Other\Other.slnx", ClaudeRequestInspector.Inspect(body, headers, 100).ProjectHint);

        headers[ClaudeRequestInspector.ProjectHeader] = @"E:\Other";
        Assert.Equal(@"E:\Other", ClaudeRequestInspector.Inspect(body, headers, 100).ProjectHint);
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

    [Theory]
    [InlineData("Current working directory: D:\\Source\\MyApp")]
    [InlineData("cwd: D:\\Source\\MyApp")]
    [InlineData("<cwd>D:\\Source\\MyApp</cwd>")]
    public void Non_claude_agent_is_orchestrated_and_its_working_directory_found(string systemLine)
    {
        var body = new JsonObject
        {
            ["model"] = "some-model",
            ["stream"] = true,
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "read" } },
            ["system"] = $"You are an expert coding assistant.\n{systemLine}\n",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Add a description column" } }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
    }

    // Shape captured from oh-my-pi 18.4.4: no vendor name, cwd only in a reminder block of the user turn, session in
    // metadata.user_id as JSON and in X-Claude-Code-Session-Id.
    [Fact]
    public void Oh_my_pi_request_is_orchestrated_with_quoted_working_directory()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-sonnet-4-5",
            ["stream"] = true,
            ["metadata"] = new JsonObject { ["user_id"] = "{\"session_id\":\"01a0f155-469a-7589-b1e2-5d6d3133261e\"}" },
            ["system"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = "You are omp's trusted coding assistant." } },
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "read" }, new JsonObject { ["name"] = "bash" } },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "<system-reminder>\nToday: 2026-09-30; current working directory: 'D:/Source/My App'. Do not repeat this information in your reply.\n</system-reminder>" },
                        new JsonObject { ["type"] = "text", ["text"] = "hello" }
                    }
                }
            }
        };

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("D:/Source/My App", info.WorkingDirectory);
        Assert.Equal("01a0f155-469a-7589-b1e2-5d6d3133261e", info.SessionId);
        Assert.Equal("hello", info.Prompt);
    }

    [Fact]
    public void Agent_without_any_vendor_marker_or_directory_is_still_orchestratable()
    {
        var body = new JsonObject
        {
            ["model"] = "some-model",
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "read" } },
            ["system"] = "You are a helpful assistant.",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "hello" } }
        };

        Assert.True(ClaudeRequestInspector.Inspect(body, NoHeaders, 100).IsOrchestratable);
    }

    [Fact]
    public void Generic_session_headers_are_read_in_priority_order()
    {
        var body = new JsonObject
        {
            ["tools"] = new JsonArray { new JsonObject { ["name"] = "read" } },
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "go" } }
        };
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-session-id"] = "generic",
            [ClaudeRequestInspector.SessionHeader] = "explicit"
        };

        Assert.Equal("explicit", ClaudeRequestInspector.Inspect(body, headers, 100).SessionId);
        headers.Remove(ClaudeRequestInspector.SessionHeader);
        Assert.Equal("generic", ClaudeRequestInspector.Inspect(body, headers, 100).SessionId);
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

    [Fact]
    public void Thread_anchors_in_replies_are_found_and_history_can_start_after_one()
    {
        var first = Guid.Parse("3f2a1b9c-0000-4000-8000-000000000001");
        var second = Guid.Parse("7d41e0aa-0000-4000-8000-000000000002");
        var body = MainAgentRequest(
        [
            new JsonObject { ["role"] = "user", ["content"] = "Add a description column" },
            Reply($"{ThreadAnchor.Line("MyApp", first)}\n🧠 Planner …\n\n---\n\nDone."),
            new JsonObject { ["role"] = "user", ["content"] = "Also validate it" },
            Reply($"{ThreadAnchor.Line("MyApp", second)}\n\n---\n\nValidated."),
            new JsonObject { ["role"] = "user", ["content"] = "quick question outside AIThread" },
            Reply("An answer that did not go through AIThread."),
            new JsonObject { ["role"] = "user", ["content"] = "Now add a unit test" }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.Equal([new ThreadAnchorRef("3f2a1b9c", 1), new ThreadAnchorRef("7d41e0aa", 3)], info.Anchors);

        var sinceSecond = ClaudeRequestInspector.HistoryAfter(body, 3);
        Assert.Contains("quick question outside AIThread", sinceSecond);
        Assert.Contains("did not go through AIThread", sinceSecond);
        Assert.DoesNotContain("Validated", sinceSecond);
        Assert.DoesNotContain("Now add a unit test", sinceSecond);

        // Nothing happened after the latest reply but the new prompt itself.
        Assert.Null(ClaudeRequestInspector.HistoryAfter(body, 5));
    }

    [Fact]
    public void Image_attached_to_the_active_turn_is_extracted()
    {
        var body = MainAgentRequest(
        [
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "Field apa saja yang disable di sini?" },
                    new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = "aGVsbG8=" }
                    }
                }
            }
        ]);

        var info = ClaudeRequestInspector.Inspect(body, NoHeaders, 100);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("Field apa saja yang disable di sini?", info.Prompt);
        var image = Assert.Single(info.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("aGVsbG8=", image.Data);
    }

    [Fact]
    public void Non_orchestratable_turn_has_no_images()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-haiku-4-5",
            ["system"] = "Generate a title",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = "aGVsbG8=" }
                        }
                    }
                }
            }
        };

        Assert.Empty(ClaudeRequestInspector.Inspect(body, NoHeaders, 100).Images);
    }

    private static JsonObject Reply(string text) => new()
    {
        ["role"] = "assistant",
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }
    };
}

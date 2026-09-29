using System.Text.Json.Nodes;
using AiThread.Service.Implementations;

namespace AiThread.Tests;

public class ClaudeHookConfigTests
{
    private static JsonObject SettingsWithUserHook() => (JsonObject)JsonNode.Parse("""
        {
          "env": { "FOO": "bar" },
          "hooks": {
            "SessionStart": [
              { "matcher": "startup", "hooks": [ { "type": "command", "command": "echo hi" } ] }
            ]
          }
        }
        """)!;

    [Fact]
    public void Apply_adds_clear_hooks_next_to_the_users_own()
    {
        var settings = SettingsWithUserHook();

        ClaudeHookConfig.Apply(settings, "http://localhost:5080/");

        var start = settings["hooks"]!["SessionStart"]!.AsArray();
        Assert.Equal(2, start.Count);
        Assert.Equal("echo hi", start[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal("clear", start[1]!["matcher"]!.GetValue<string>());
        Assert.Equal("http", start[1]!["hooks"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("http://localhost:5080/api/hooks/claude/session-start", start[1]!["hooks"]![0]!["url"]!.GetValue<string>());
        Assert.Equal("http://localhost:5080/api/hooks/claude/session-end", settings["hooks"]!["SessionEnd"]![0]!["hooks"]![0]!["url"]!.GetValue<string>());
        Assert.Equal("http://localhost:5080", ClaudeHookConfig.InstalledBaseUrl(settings));
    }

    [Fact]
    public void Applying_again_replaces_the_old_address_instead_of_duplicating()
    {
        var settings = SettingsWithUserHook();

        ClaudeHookConfig.Apply(settings, "http://localhost:5080");
        ClaudeHookConfig.Apply(settings, "http://localhost:6000");

        Assert.Equal(2, settings["hooks"]!["SessionStart"]!.AsArray().Count);
        Assert.Single(settings["hooks"]!["SessionEnd"]!.AsArray());
        Assert.Equal("http://localhost:6000", ClaudeHookConfig.InstalledBaseUrl(settings));
    }

    [Fact]
    public void Remove_keeps_other_hooks_and_settings()
    {
        var settings = SettingsWithUserHook();
        ClaudeHookConfig.Apply(settings, "http://localhost:5080");

        ClaudeHookConfig.Remove(settings);

        Assert.Null(ClaudeHookConfig.InstalledBaseUrl(settings));
        Assert.Null(settings["hooks"]!["SessionEnd"]);
        Assert.Equal("echo hi", Assert.Single(settings["hooks"]!["SessionStart"]!.AsArray())!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal("bar", settings["env"]!["FOO"]!.GetValue<string>());
    }

    [Fact]
    public void Remove_drops_the_hooks_block_when_only_aithread_was_there()
    {
        var settings = new JsonObject();
        ClaudeHookConfig.Apply(settings, "http://localhost:5080");

        ClaudeHookConfig.Remove(settings);

        Assert.False(settings.ContainsKey("hooks"));
    }
}

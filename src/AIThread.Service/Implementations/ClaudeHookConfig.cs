using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

// AIThread's entries in the "hooks" block of a Claude Code settings.json: official HTTP hooks that carry a
// conversation across /clear (see SessionHandoff). Hooks the user configured themselves are never touched.
public static class ClaudeHookConfig
{
    public const string PathMarker = "/api/hooks/claude/";

    // SessionEnd hooks share a small budget that a longer timeout raises; the endpoint answers at once anyway.
    private const int SessionEndTimeoutSeconds = 5;

    // Above SessionHandoff's 40-second wait for the summary.
    private const int SessionStartTimeoutSeconds = 60;

    public static JsonObject Block(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        return new JsonObject
        {
            ["SessionEnd"] = new JsonArray { Group($"{root}{PathMarker}session-end", SessionEndTimeoutSeconds) },
            ["SessionStart"] = new JsonArray { Group($"{root}{PathMarker}session-start", SessionStartTimeoutSeconds) }
        };
    }

    // Replaces earlier AIThread entries (whatever URL they point at) and keeps everything else.
    public static void Apply(JsonObject settings, string baseUrl)
    {
        Remove(settings);
        var hooks = settings["hooks"] as JsonObject ?? new JsonObject();
        settings["hooks"] = hooks;
        foreach (var (hookEvent, groups) in Block(baseUrl))
        {
            var list = hooks[hookEvent] as JsonArray ?? new JsonArray();
            hooks[hookEvent] = list;
            foreach (var group in groups!.AsArray()) list.Add(group!.DeepClone());
        }
    }

    public static void Remove(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooks) return;
        foreach (var hookEvent in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[hookEvent] is not JsonArray groups) continue;
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i]?["hooks"] is not JsonArray handlers) continue;
                var removed = false;
                for (var j = handlers.Count - 1; j >= 0; j--)
                {
                    if (!IsAIThread(handlers[j])) continue;
                    handlers.RemoveAt(j);
                    removed = true;
                }
                if (removed && handlers.Count == 0) groups.RemoveAt(i);
            }
            if (groups.Count == 0) hooks.Remove(hookEvent);
        }
        if (hooks.Count == 0) settings.Remove("hooks");
    }

    // The base URL the installed AIThread hooks point at, or null when none is installed.
    public static string? InstalledBaseUrl(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooks) return null;
        foreach (var (_, groups) in hooks)
        {
            if (groups is not JsonArray list) continue;
            foreach (var group in list)
            {
                if (group?["hooks"] is not JsonArray handlers) continue;
                foreach (var handler in handlers)
                {
                    if (IsAIThread(handler) && Url(handler) is { } url)
                        return url[..url.IndexOf(PathMarker, StringComparison.OrdinalIgnoreCase)];
                }
            }
        }
        return null;
    }

    private static JsonObject Group(string url, int timeoutSeconds) => new()
    {
        ["matcher"] = "clear",
        ["hooks"] = new JsonArray { new JsonObject { ["type"] = "http", ["url"] = url, ["timeout"] = timeoutSeconds } }
    };

    private static bool IsAIThread(JsonNode? handler) =>
        Url(handler)?.Contains(PathMarker, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Url(JsonNode? handler) =>
        handler?["url"] is JsonValue value && value.TryGetValue<string>(out var url) ? url : null;
}

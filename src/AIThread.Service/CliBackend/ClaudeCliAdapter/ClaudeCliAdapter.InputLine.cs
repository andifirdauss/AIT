using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The user turn as one stream-json line on stdin (no command-line length limit, and images go as content blocks).
    public static string InputLine(CliPrompt prompt)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrWhiteSpace(prompt.User) ? "Continue." : prompt.User }
        };
        foreach (var image in prompt.Images)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = image.Data }
            });
        }
        return new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = content }
        }.ToJsonString();
    }
}

using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // Gemini CLI's built-in tools, denied by name as well in case a version ignores an empty tools.core.
    private static readonly string[] BuiltInTools =
    [
        "run_shell_command", "write_file", "replace", "read_file", "read_many_files", "list_directory", "glob",
        "search_file_content", "grep_search", "google_web_search", "web_fetch", "save_memory", "write_todos",
        "activate_skill", "codebase_investigator", "delegate_to_agent"
    ];

    // .gemini/settings.json of the empty working folder (workspace settings, above the user's): no tool at all (an empty
    // tools.core allowlist), no MCP server, and no GEMINI.md context file (a name that never exists). System and
    // managed settings stay above it.
    public static string WorkspaceSettings() => new JsonObject
    {
        ["tools"] = new JsonObject
        {
            ["core"] = new JsonArray(),
            ["exclude"] = new JsonArray(BuiltInTools.Select(t => (JsonNode)JsonValue.Create(t)).ToArray())
        },
        ["mcpServers"] = new JsonObject(),
        ["context"] = new JsonObject { ["fileName"] = "AIT-NO-CONTEXT.md" }
    }.ToJsonString();
}

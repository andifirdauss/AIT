namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // One headless model turn: stream-json events, no extension, no MCP server (an allowlist naming none). The prompt
    // comes on stdin (piped stdin is headless mode), the system prompt through GEMINI_SYSTEM_MD, and the tools are
    // denied by the workspace settings (WorkspaceSettings).
    public static IReadOnlyList<string> BuildArguments(string model)
    {
        var arguments = new List<string>
        {
            "--output-format", "stream-json",
            "--extensions", "none",
            "--allowed-mcp-server-names", "none"
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }
        return arguments;
    }
}

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // One stateless model turn: no built-in tools, no MCP server, no skill or slash command, no saved session, no user
    // settings (see ClaudeCliOptions.SettingSources), and the agent's own system prompt instead of Claude Code's.
    private IReadOnlyList<string> BuildArguments(string model, string systemPromptFile)
    {
        var sources = string.IsNullOrWhiteSpace(options.Value.ClaudeCli.SettingSources) ? "local" : options.Value.ClaudeCli.SettingSources.Trim();
        var arguments = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--system-prompt-file", systemPromptFile,
            "--tools", "",
            "--strict-mcp-config",
            "--disable-slash-commands",
            "--setting-sources", sources,
            "--no-session-persistence",
            "--max-turns", "1"
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }
        return arguments;
    }
}

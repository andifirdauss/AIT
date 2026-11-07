namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // A -c value as a TOML literal string: backslashes of a Windows path stay as they are. A path holding a single quote
    // falls back to a basic string with escapes.
    private static string TomlLiteral(string value) =>
        value.Contains('\'')
            ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : "'" + value + "'";
}

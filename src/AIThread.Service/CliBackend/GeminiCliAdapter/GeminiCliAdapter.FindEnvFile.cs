namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // The .env Gemini CLI loads: walking up from the working folder, <dir>/.gemini/.env then <dir>/.env; at the root,
    // ~/.gemini/.env then ~/.env.
    public static string? FindEnvFile(string startFolder, string home)
    {
        var current = Path.GetFullPath(startFolder);
        while (true)
        {
            if (CliProcess.Existing(Path.Combine(current, ".gemini", ".env")) is { } gemini) return gemini;
            if (CliProcess.Existing(Path.Combine(current, ".env")) is { } plain) return plain;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        return CliProcess.Existing(Path.Combine(home, ".gemini", ".env")) ?? CliProcess.Existing(Path.Combine(home, ".env"));
    }
}

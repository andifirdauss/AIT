namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // One headless turn: the prompt as one stream-json line on stdin, stream-json events out, slash commands off, the
    // workspace agent without tools, and the deny-all permission settings (a file: a JSON argument would need Windows
    // quoting).
    public static IReadOnlyList<string> BuildArguments(string model, string settingsFile)
    {
        var arguments = new List<string>
        {
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--disable-slash-commands",
            "--agent", AgentName,
            "--settings", settingsFile
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }
        return arguments;
    }
}

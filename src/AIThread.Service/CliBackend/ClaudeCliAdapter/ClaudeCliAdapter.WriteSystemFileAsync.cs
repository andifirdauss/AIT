namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static async Task<string> WriteSystemFileAsync(string work, string system, CancellationToken cancellationToken)
    {
        var systemFile = Path.Combine(work, "system-prompt.md");
        await File.WriteAllTextAsync(systemFile, system.Length == 0 ? "You are a helpful assistant." : system, CliProcess.Utf8, cancellationToken);
        return systemFile;
    }
}

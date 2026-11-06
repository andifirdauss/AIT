namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static string Shown(string model) => model.Length == 0 ? "default" : model;
}

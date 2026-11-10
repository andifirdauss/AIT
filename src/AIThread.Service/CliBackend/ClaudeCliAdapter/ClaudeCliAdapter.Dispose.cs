namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    public void Dispose() => _warm.Dispose();
}

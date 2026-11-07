namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
}

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
}

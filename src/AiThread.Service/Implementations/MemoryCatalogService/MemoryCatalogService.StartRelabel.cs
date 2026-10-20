namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public void StartRelabel() =>
        _ = Task.Run(() => relabeler.RunAsync(false, lifetime.ApplicationStopping), CancellationToken.None);
}

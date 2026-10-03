using AiThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;

namespace AiThread.Service.Background;

// Puts the memory stores chosen in the Setting menu in use before the workers that read memory start.
// Migrations have already run at this point (InitializeDataLayerAsync is awaited before the host starts).
public sealed class MemoryStorageInitializer(IMemoryStorageService memoryStorage) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => memoryStorage.ApplySavedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

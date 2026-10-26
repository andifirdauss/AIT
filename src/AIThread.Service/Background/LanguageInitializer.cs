using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;

namespace AIThread.Service.Background;

// Reads the language chosen in the Setting menu (loading the settings applies it to Lang) before anything shows text.
public sealed class LanguageInitializer(ISettingService settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => settings.GetRuntimeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

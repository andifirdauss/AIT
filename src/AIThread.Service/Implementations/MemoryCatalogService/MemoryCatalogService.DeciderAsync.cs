namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<string> DeciderAsync(CancellationToken cancellationToken) =>
        (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
}

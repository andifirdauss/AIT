using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    public async Task<MemoryStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(cancellationToken);
        return new MemoryStorageSettingsDto(
            ToDto(entity.ShortMemoryStore),
            ToDto(entity.LongMemoryStore),
            new MemoryStoreStatusDto(shortStore.IsConnected, shortStore.Description),
            new MemoryStoreStatusDto(await LongConnectedAsync(cancellationToken), longStore.Description),
            ToDto(entity.BusinessMemoryStore),
            new MemoryStoreStatusDto(await BusinessConnectedAsync(cancellationToken), businessStore.Description));
    }
}

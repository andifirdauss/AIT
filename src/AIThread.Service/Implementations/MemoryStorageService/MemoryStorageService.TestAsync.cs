using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    public async Task<MemoryStoreTestResultDto> TestAsync(TestMemoryStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Level)) throw new AppValidationException(Lang.T("Level memory tidak valid.", "Invalid memory level."));

        var entity = await LoadAsync(cancellationToken);
        var saved = request.Level switch
        {
            MemoryLevel.Short => entity.ShortMemoryStore,
            MemoryLevel.Business => entity.BusinessMemoryStore,
            _ => entity.LongMemoryStore
        };
        var (_, connection) = Normalize(request.Store, request.Level, saved);

        if (connection.Provider != MemoryStoreProvider.Default)
        {
            var result = await MemoryStoreTester.TestAsync(request.Level, connection, cancellationToken);
            return new MemoryStoreTestResultDto(result.Success, result.LatencyMs, result.Message);
        }

        // The built-in stores: report their live state.
        var started = DateTime.UtcNow;
        var connected = request.Level == MemoryLevel.Short ? IsDefaultShortConnected() : await MainDatabaseConnectedAsync(cancellationToken);
        var latency = (long)(DateTime.UtcNow - started).TotalMilliseconds;
        var name = request.Level == MemoryLevel.Short ? Lang.T("Garnet bawaan", "Built-in Garnet") : Lang.T("Database utama AIT", "Main AIT database");
        return new MemoryStoreTestResultDto(connected, latency, connected ? Lang.T($"{name} terhubung.", $"{name} connected.") : Lang.T($"{name} tidak terhubung.", $"{name} is not connected."));
    }
}

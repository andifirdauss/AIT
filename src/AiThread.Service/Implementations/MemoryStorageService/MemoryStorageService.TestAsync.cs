using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Data.Memory;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    public async Task<MemoryStoreTestResultDto> TestAsync(TestMemoryStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Level)) throw new AppValidationException("Level memory tidak valid.");

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
        var name = request.Level == MemoryLevel.Short ? "Garnet bawaan" : "Database utama AIT";
        return new MemoryStoreTestResultDto(connected, latency, connected ? $"{name} terhubung." : $"{name} tidak terhubung.");
    }
}

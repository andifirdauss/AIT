using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is { } cached) return cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } again) return again;
            var entity = await LoadOrCreateAsync(cancellationToken);
            _cache = ToRuntime(entity);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}

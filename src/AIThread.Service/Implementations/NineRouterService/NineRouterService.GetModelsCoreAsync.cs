using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private async Task<IReadOnlyList<RouterModelDto>> GetModelsCoreAsync(string baseUrl, bool refresh, CancellationToken cancellationToken)
    {
        var cache = _modelCache;
        if (!refresh && cache is not null && cache.BaseUrl == baseUrl && DateTime.UtcNow - cache.LoadedAt < ModelCacheDuration)
            return cache.Models;

        await _modelLock.WaitAsync(cancellationToken);
        try
        {
            cache = _modelCache;
            if (!refresh && cache is not null && cache.BaseUrl == baseUrl && DateTime.UtcNow - cache.LoadedAt < ModelCacheDuration)
                return cache.Models;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Client().GetAsync(Combine(baseUrl, "/v1/models"), timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new NineRouterException($"GET /v1/models HTTP {(int)response.StatusCode}: {ExtractError(json)}", (int)response.StatusCode);

            var models = ParseModels(json);
            _modelCache = new ModelCache(baseUrl, models, DateTime.UtcNow);
            return models;
        }
        finally
        {
            _modelLock.Release();
        }
    }
}

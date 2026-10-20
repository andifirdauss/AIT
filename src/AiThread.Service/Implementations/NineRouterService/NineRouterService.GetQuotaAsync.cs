using System.Text.Json;
using AiThread.Common.DTOs;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // One account; null when it has nothing to show or 9router cannot read it right now.
    private async Task<RouterQuotaDto?> GetQuotaAsync(string baseUrl, RouterConnection connection, IReadOnlySet<string> usedModels, CancellationToken cancellationToken)
    {
        var key = $"{baseUrl}|{connection.Id}";
        try
        {
            if (!_usageCache.TryGetValue(key, out var cached) || DateTime.UtcNow - cached.LoadedAt >= QuotaCacheDuration)
            {
                cached = new UsageCache(await GetDashboardJsonAsync(baseUrl, $"/api/usage/{Uri.EscapeDataString(connection.Id)}", cancellationToken), DateTime.UtcNow);
                _usageCache[key] = cached;
            }
            return RouterQuotas.ParseUsage(cached.Json, connection, usedModels);
        }
        // A slow account (the shared timeout) leaves the others' results standing.
        catch (Exception ex) when (ex is HttpRequestException or NineRouterException or JsonException or OperationCanceledException)
        {
            logger.LogDebug("9router usage of {Provider} ({Name}) unavailable: {Message}", connection.Provider, connection.Name, ex.Message);
            return null;
        }
    }
}

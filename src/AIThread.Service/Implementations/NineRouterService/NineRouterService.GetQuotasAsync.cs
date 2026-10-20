using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<IReadOnlyList<RouterQuotaDto>> GetQuotasAsync(IReadOnlyCollection<string> models, CancellationToken cancellationToken = default)
    {
        if (models.Count == 0) return [];
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var baseUrl = settings.NineRouterBaseUrl;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QuotaTimeout);
        try
        {
            // A model id without a provider prefix can only be a combo.
            var combos = models.Any(m => !m.Contains('/'))
                ? RouterQuotas.ParseCombos(await GetDashboardJsonAsync(baseUrl, "/api/combos", timeout.Token))
                : new Dictionary<string, IReadOnlyList<string>>();
            var modelsByPrefix = RouterQuotas.ModelsByPrefix(models, combos);
            if (modelsByPrefix.Count == 0) return [];

            var connections = new List<RouterConnection>();
            for (int page = 1, pages = 1; page <= pages && page <= MaxConnectionPages; page++)
                connections.AddRange(RouterQuotas.ParseConnections(
                    await GetDashboardJsonAsync(baseUrl, $"/api/providers/client?pageSize=100&page={page}", timeout.Token), out pages));

            var lookups = connections
                .Select(c => (Connection: c, Used: RouterQuotas.UsedModels(c.Provider, modelsByPrefix)))
                .Where(c => c.Used is not null)
                .Select(c => GetQuotaAsync(baseUrl, c.Connection, c.Used!, timeout.Token));
            return (await Task.WhenAll(lookups)).OfType<RouterQuotaDto>().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or NineRouterException or JsonException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug("9router quota lookup skipped: {Message}", ex.Message);
            return [];
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private async Task<RouterStatusDto> ProbeAsync(string baseUrl, string? apiKey, bool useCache, CancellationToken cancellationToken)
    {
        var hasKey = !string.IsNullOrEmpty(apiKey);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using (var health = await Client().GetAsync(Combine(baseUrl, "/api/health"), timeout.Token))
            {
                stopwatch.Stop();
                // Older builds or reverse proxies may not expose /api/health; the model list below still proves liveness.
                if (!health.IsSuccessStatusCode && health.StatusCode != HttpStatusCode.NotFound)
                    return Offline(baseUrl, hasKey, $"Health check HTTP {(int)health.StatusCode}");
            }

            var models = await GetModelsCoreAsync(baseUrl, refresh: !useCache, cancellationToken);

            // Health and the model list are public, so "online" says nothing about auth. ApiKeyValid answers
            // "will chat requests authenticate?": false also when no key is stored but 9router requires one.
            var accepted = await IsAuthenticationAcceptedAsync(baseUrl, hasKey ? apiKey : null, useCache, cancellationToken);
            bool? keyValid = hasKey ? accepted : accepted ? null : false;

            return new RouterStatusDto(
                true,
                baseUrl,
                stopwatch.ElapsedMilliseconds,
                models.Count,
                models.Count(m => m.IsCombo),
                hasKey,
                keyValid,
                accepted ? null : hasKey ? KeyRejectedMessage : KeyMissingMessage,
                DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or NineRouterException or JsonException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Offline(baseUrl, hasKey, ex is OperationCanceledException ? Lang.T("Timeout (5 detik)", "Timeout (5 seconds)") : ex.Message);
        }
    }
}

using System.Text.Json;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    private async Task<T> CallAsync<T>(string token, string method, object payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            // Serialized up front so the request carries a Content-Length: some proxies and self-hosted Bot API
            // servers do not read chunked request bodies, and would then never see the getUpdates offset.
            using var content = new StringContent(JsonSerializer.Serialize(payload, payload.GetType(), Json), Encoding.UTF8, "application/json");
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .PostAsync($"{BaseUrl}/bot{token}/{method}", content, timeoutSource.Token);
            var json = await response.Content.ReadAsStringAsync(timeoutSource.Token);

            TgResponse<T>? body = null;
            try
            {
                body = JsonSerializer.Deserialize<TgResponse<T>>(json, Json);
            }
            catch (JsonException)
            {
                // Not a Bot API answer (proxy error page, wrong ApiBaseUrl); reported below with the HTTP status.
            }

            if (body is { Ok: true, Result: { } result }) return result;

            var description = body?.Description ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            var retryAfter = body?.Parameters?.RetryAfter is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
            throw new TelegramApiException($"{method}: {description}", body?.ErrorCode ?? (int)response.StatusCode, retryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TelegramApiException(Lang.T($"{method}: Telegram tidak merespons dalam {timeout.TotalSeconds:0} detik.", $"{method}: Telegram did not respond within {timeout.TotalSeconds:0} seconds."));
        }
        catch (HttpRequestException ex)
        {
            throw new TelegramApiException(Lang.T($"{method}: Telegram tidak dapat dihubungi ({ex.Message}).", $"{method}: Telegram cannot be reached ({ex.Message})."), innerException: ex);
        }
    }
}

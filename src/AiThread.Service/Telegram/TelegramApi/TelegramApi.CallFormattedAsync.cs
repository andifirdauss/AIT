using AiThread.Service.Models;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Resends as plain text when Telegram rejects the markup, and waits when flood control asks for a short pause.
    private async Task<T> CallFormattedAsync<T>(string token, string method, Func<bool, object> payload, CancellationToken cancellationToken)
    {
        var asHtml = true;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CallAsync<T>(token, method, payload(asHtml), CallTimeout, cancellationToken);
            }
            catch (TelegramApiException ex) when (ex.IsParseError && asHtml)
            {
                asHtml = false;
            }
            catch (TelegramApiException ex) when (ex.RetryAfter is { } wait && wait <= MaxRetryAfter && attempt < 3)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }
    }
}

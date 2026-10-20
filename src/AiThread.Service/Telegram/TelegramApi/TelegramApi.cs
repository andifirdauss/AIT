using System.Text.Json;
using System.Text.Json.Serialization;
using AiThread.Common.Options;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Telegram;

// Minimal client for the Telegram Bot API (https://core.telegram.org/bots/api). Every method is a JSON POST to
// {ApiBaseUrl}/bot{token}/{method}, answered with {"ok":true,"result":...} or {"ok":false,"error_code":..,"description":..}.
// The token is read from the runtime settings on every call, so a new token applies without a restart. It is part of
// the URL: never log request URIs of this client (its HttpClient loggers are removed in DI).
public sealed partial class TelegramApi(IHttpClientFactory httpClientFactory, ISettingService settingService, IOptions<AiThreadOptions> options) : ITelegramApi
{
    public const string HttpClientName = "Telegram";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    private static readonly string[] AllowedUpdates = ["message", "callback_query"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string BaseUrl => options.Value.Telegram.ApiBaseUrl.TrimEnd('/');
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiThread.Common.Options;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Telegram;

// Minimal client for the Telegram Bot API (https://core.telegram.org/bots/api). Every method is a JSON POST to
// {ApiBaseUrl}/bot{token}/{method}, answered with {"ok":true,"result":...} or {"ok":false,"error_code":..,"description":..}.
// The token is read from the runtime settings on every call, so a new token applies without a restart. It is part of
// the URL: never log request URIs of this client (its HttpClient loggers are removed in DI).
public sealed partial class TelegramApi(IHttpClientFactory httpClientFactory, ISettingService settingService, IOptions<AiThreadOptions> options)
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

    // BotFather tokens look like "123456789:AAE…"; anything else is rejected before calling Telegram.
    public static bool IsValidTokenFormat(string? token) => token is not null && TokenFormat().IsMatch(token);

    // Explicit token so a token can be verified before it is saved.
    public Task<TgUser> GetMeAsync(string token, CancellationToken cancellationToken) =>
        CallAsync<TgUser>(token, "getMe", new { }, CallTimeout, cancellationToken);

    // getUpdates does not work while a webhook is set (e.g. by another tool that used the same bot before).
    public async Task DeleteWebhookAsync(CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "deleteWebhook", new { DropPendingUpdates = false }, CallTimeout, cancellationToken);

    public async Task SetCommandsAsync(IEnumerable<TelegramCommand> commands, CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "setMyCommands",
            new { Commands = commands.Select(c => new { c.Command, c.Description }).ToList() }, CallTimeout, cancellationToken);

    // Long poll: Telegram holds the request up to timeoutSeconds until an update arrives.
    public async Task<IReadOnlyList<TgUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        await CallAsync<List<TgUpdate>>(await TokenAsync(cancellationToken), "getUpdates",
            new { Offset = offset, Timeout = timeoutSeconds, AllowedUpdates }, TimeSpan.FromSeconds(timeoutSeconds + 15), cancellationToken);

    // Returns the id of the sent message.
    public async Task<long> SendAsync(long chatId, string html, long? replyToMessageId = null, TgInlineKeyboard? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(cancellationToken);
        var message = await CallFormattedAsync<TgMessage>(token, "sendMessage", asHtml => new
        {
            ChatId = chatId,
            Text = asHtml ? html : TelegramFormatter.ToPlainText(html),
            ParseMode = asHtml ? "HTML" : null,
            LinkPreviewOptions = new { IsDisabled = true },
            ReplyParameters = replyToMessageId is { } id ? new { MessageId = id, AllowSendingWithoutReply = true } : null,
            ReplyMarkup = keyboard
        }, cancellationToken);
        return message.MessageId;
    }

    // Replaces the text of a message the bot sent. A null keyboard removes the buttons. "Not modified" is not an error.
    public async Task EditAsync(long chatId, long messageId, string html, TgInlineKeyboard? keyboard = null, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(cancellationToken);
        try
        {
            await CallFormattedAsync<JsonElement>(token, "editMessageText", asHtml => new
            {
                ChatId = chatId,
                MessageId = messageId,
                Text = asHtml ? html : TelegramFormatter.ToPlainText(html),
                ParseMode = asHtml ? "HTML" : null,
                LinkPreviewOptions = new { IsDisabled = true },
                ReplyMarkup = keyboard
            }, cancellationToken);
        }
        catch (TelegramApiException ex) when (ex.IsNotModified)
        {
        }
    }

    // Stops the loading indicator on the pressed button; text is shown as a short toast.
    public async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "answerCallbackQuery",
            new { CallbackQueryId = callbackQueryId, Text = text }, CallTimeout, cancellationToken);

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
            throw new TelegramApiException($"{method}: Telegram tidak merespons dalam {timeout.TotalSeconds:0} detik.");
        }
        catch (HttpRequestException ex)
        {
            throw new TelegramApiException($"{method}: Telegram tidak dapat dihubungi ({ex.Message}).", innerException: ex);
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken) =>
        (await settingService.GetRuntimeAsync(cancellationToken)).TelegramBotToken
        ?? throw new TelegramApiException("Bot token Telegram belum diisi (menu Setting).");

    private string BaseUrl => options.Value.Telegram.ApiBaseUrl.TrimEnd('/');

    [GeneratedRegex(@"^\d{3,}:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex TokenFormat();
}

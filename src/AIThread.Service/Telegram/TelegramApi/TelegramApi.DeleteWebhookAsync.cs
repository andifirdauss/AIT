namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // getUpdates does not work while a webhook is set (e.g. by another tool that used the same bot before).
    public async Task DeleteWebhookAsync(CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "deleteWebhook", new { DropPendingUpdates = false }, CallTimeout, cancellationToken);
}

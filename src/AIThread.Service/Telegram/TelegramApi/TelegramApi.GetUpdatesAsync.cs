namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Long poll: Telegram holds the request up to timeoutSeconds until an update arrives.
    public async Task<IReadOnlyList<TgUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        await CallAsync<List<TgUpdate>>(await TokenAsync(cancellationToken), "getUpdates",
            new { Offset = offset, Timeout = timeoutSeconds, AllowedUpdates }, TimeSpan.FromSeconds(timeoutSeconds + 15), cancellationToken);
}

namespace AiThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Explicit token so a token can be verified before it is saved.
    public Task<TgUser> GetMeAsync(string token, CancellationToken cancellationToken) =>
        CallAsync<TgUser>(token, "getMe", new { }, CallTimeout, cancellationToken);
}

namespace AiThread.Service.Telegram;

public sealed partial class TelegramApi
{
    public async Task SetCommandsAsync(IEnumerable<TelegramCommand> commands, CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "setMyCommands",
            new { Commands = commands.Select(c => new { c.Command, c.Description }).ToList() }, CallTimeout, cancellationToken);
}

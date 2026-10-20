using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task HandleCommandAsync(TelegramChat chat, string text, CancellationToken cancellationToken)
    {
        var (command, argument) = TelegramMessages.ParseCommand(text);
        switch (command)
        {
            case "start":
            case "help":
                await api.SendAsync(chat.ChatId, TelegramMessages.Help(await ActiveProjectNameAsync(chat, cancellationToken)), cancellationToken: cancellationToken);
                break;
            case "projects":
            case "project":
                if (argument.Length == 0) await SendProjectMenuAsync(chat, null, cancellationToken);
                else await SelectProjectByNameAsync(chat, argument, cancellationToken);
                break;
            case "status":
                await SendStatusAsync(chat, cancellationToken);
                break;
            case "cancel":
                await CancelAsync(chat, argument, cancellationToken);
                break;
            case "diff":
                await SendDiffAsync(chat, argument, cancellationToken);
                break;
            case "new":
                await UpdateChatAsync(chat.ChatId, c => c.ConversationStartedAt = DateTime.UtcNow, cancellationToken);
                await api.SendAsync(chat.ChatId, TelegramMessages.ConversationReset, cancellationToken: cancellationToken);
                break;
            default:
                await api.SendAsync(chat.ChatId, TelegramMessages.UnknownCommand, cancellationToken: cancellationToken);
                break;
        }
    }
}

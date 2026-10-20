using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task SendProjectMenuAsync(TelegramChat chat, string? intro, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (projects.Count == 0)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.NoProjects, cancellationToken: cancellationToken);
            return;
        }

        await api.SendAsync(chat.ChatId, TelegramMessages.ProjectList(projects, chat.ActiveProjectId, intro),
            keyboard: TelegramMessages.ProjectKeyboard(projects, chat.ActiveProjectId), cancellationToken: cancellationToken);
    }
}

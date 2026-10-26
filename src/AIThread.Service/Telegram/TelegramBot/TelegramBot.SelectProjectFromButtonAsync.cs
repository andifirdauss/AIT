using AIThread.Common.Entities;
using AIThread.Common.Localization;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task SelectProjectFromButtonAsync(TelegramChat chat, TgCallbackQuery callback, Guid projectId, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (projects.FirstOrDefault(p => p.Id == projectId) is not { } project)
        {
            await api.AnswerCallbackAsync(callback.Id, Lang.T("Project sudah tidak terdaftar.", "The project is no longer registered."), cancellationToken);
            return;
        }

        await SetActiveProjectAsync(chat, project, cancellationToken);
        await api.AnswerCallbackAsync(callback.Id, TelegramFormatter.Truncate(Lang.T($"Project aktif: {project.Name}", $"Active project: {project.Name}"), 200), cancellationToken);

        var pending = TakePendingPrompt(chat.ChatId);
        if (callback.Message is { } menu)
            await api.EditAsync(chat.ChatId, menu.MessageId,
                TelegramMessages.ProjectList(projects, project.Id, TelegramMessages.ProjectSelected(project.Name, pending is not null)),
                TelegramMessages.ProjectKeyboard(projects, project.Id), cancellationToken);
        if (pending is not null) await StartThreadAsync(chat, pending.MessageId, pending.Prompt, cancellationToken);
    }
}

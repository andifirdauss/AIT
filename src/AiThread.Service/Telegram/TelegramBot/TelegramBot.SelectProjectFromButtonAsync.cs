using AiThread.Common.Entities;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task SelectProjectFromButtonAsync(TelegramChat chat, TgCallbackQuery callback, Guid projectId, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (projects.FirstOrDefault(p => p.Id == projectId) is not { } project)
        {
            await api.AnswerCallbackAsync(callback.Id, "Project sudah tidak terdaftar.", cancellationToken);
            return;
        }

        await SetActiveProjectAsync(chat, project, cancellationToken);
        await api.AnswerCallbackAsync(callback.Id, TelegramFormatter.Truncate($"Project aktif: {project.Name}", 200), cancellationToken);

        var pending = TakePendingPrompt(chat.ChatId);
        if (callback.Message is { } menu)
            await api.EditAsync(chat.ChatId, menu.MessageId,
                TelegramMessages.ProjectList(projects, project.Id, TelegramMessages.ProjectSelected(project.Name, pending is not null)),
                TelegramMessages.ProjectKeyboard(projects, project.Id), cancellationToken);
        if (pending is not null) await StartThreadAsync(chat, pending.MessageId, pending.Prompt, cancellationToken);
    }
}

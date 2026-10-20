using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // "/project 2" (number from the list) or "/project name" (exact, else a unique partial match).
    private async Task SelectProjectByNameAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        var project = int.TryParse(argument, out var number) && number >= 1 && number <= projects.Count
            ? projects[number - 1]
            : projects.FirstOrDefault(p => string.Equals(p.Name, argument, StringComparison.OrdinalIgnoreCase))
              ?? (projects.Where(p => p.Name.Contains(argument, StringComparison.OrdinalIgnoreCase)).ToList() is [var single] ? single : null);
        if (project is null)
        {
            await SendProjectMenuAsync(chat, TelegramMessages.ProjectNotFound(argument), cancellationToken);
            return;
        }

        await SetActiveProjectAsync(chat, project, cancellationToken);
        var pending = TakePendingPrompt(chat.ChatId);
        await api.SendAsync(chat.ChatId, TelegramMessages.ProjectSelected(project.Name, pending is not null), cancellationToken: cancellationToken);
        if (pending is not null) await StartThreadAsync(chat, pending.MessageId, pending.Prompt, cancellationToken);
    }
}

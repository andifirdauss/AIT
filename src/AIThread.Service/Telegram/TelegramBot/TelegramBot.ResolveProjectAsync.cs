using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // The chat's active project; when none is set and exactly one project is registered, that one.
    private async Task<TelegramProject?> ResolveProjectAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (chat.ActiveProjectId is { } id && projects.FirstOrDefault(p => p.Id == id) is { } active) return active;
        if (projects.Count != 1) return null;

        await SetActiveProjectAsync(chat, projects[0], cancellationToken);
        return projects[0];
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // Switching project starts a new conversation: history from another codebase would only mislead the planner.
    private async Task SetActiveProjectAsync(TelegramChat chat, TelegramProject project, CancellationToken cancellationToken)
    {
        if (chat.ActiveProjectId == project.Id) return;

        var now = DateTime.UtcNow;
        await UpdateChatAsync(chat.ChatId, c =>
        {
            c.ActiveProjectId = project.Id;
            c.ConversationStartedAt = now;
        }, cancellationToken);
        chat.ActiveProjectId = project.Id;
        chat.ConversationStartedAt = now;
        eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "project" }));
    }
}

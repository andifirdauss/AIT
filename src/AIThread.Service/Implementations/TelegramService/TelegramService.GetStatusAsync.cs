using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService
{
    public async Task<TelegramStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetTelegramAsync(cancellationToken);
        var state = runtime.Snapshot;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chats = await db.TelegramChats.AsNoTracking()
            .OrderBy(c => c.IsAllowed)
            .ThenByDescending(c => c.LastMessageAt)
            .Select(c => new TelegramChatDto(
                c.Id, c.ChatId, c.DisplayName, c.Username, c.IsAllowed, c.ActiveProjectId,
                c.ActiveProject != null ? c.ActiveProject.Name : null, c.CreatedAt, c.LastMessageAt))
            .ToListAsync(cancellationToken);

        return new TelegramStatusDto(
            settings.Enabled, settings.HasBotToken, settings.BotTokenMasked,
            state.Status, state.BotUsername, state.Error, state.LastPollAt, chats);
    }
}

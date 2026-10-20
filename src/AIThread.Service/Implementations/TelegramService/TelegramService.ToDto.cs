using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService
{
    private static TelegramChatDto ToDto(TelegramChat c) => new(
        c.Id, c.ChatId, c.DisplayName, c.Username, c.IsAllowed, c.ActiveProjectId, c.ActiveProject?.Name, c.CreatedAt, c.LastMessageAt);
}

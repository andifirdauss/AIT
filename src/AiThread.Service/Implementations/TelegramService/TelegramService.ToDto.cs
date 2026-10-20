using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class TelegramService
{
    private static TelegramChatDto ToDto(TelegramChat c) => new(
        c.Id, c.ChatId, c.DisplayName, c.Username, c.IsAllowed, c.ActiveProjectId, c.ActiveProject?.Name, c.CreatedAt, c.LastMessageAt);
}

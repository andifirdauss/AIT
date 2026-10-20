using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Setting page side of the Telegram bot: token, on/off, and which chats may send prompts.
public interface ITelegramService
{
    Task<TelegramStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    // A new token is verified with Telegram (getMe) before it is stored; the poller restarts with the new settings.
    Task<TelegramStatusDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default);

    Task<TelegramChatDto?> AllowChatAsync(Guid id, CancellationToken cancellationToken = default);

    // Removing an allowed chat revokes its access; it shows up as waiting again if it writes to the bot.
    Task<bool> DeleteChatAsync(Guid id, CancellationToken cancellationToken = default);
}

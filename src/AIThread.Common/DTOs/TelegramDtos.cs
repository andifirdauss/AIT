using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

public record TelegramSettingsDto(bool Enabled, bool HasBotToken, string? BotTokenMasked);

// BotToken empty = keep the stored token.
public record UpdateTelegramSettingsRequest(bool Enabled, string? BotToken, bool ClearBotToken);

public record TelegramChatDto(
    Guid Id,
    long ChatId,
    string DisplayName,
    string? Username,
    bool IsAllowed,
    Guid? ActiveProjectId,
    string? ActiveProjectName,
    DateTime CreatedAt,
    DateTime LastMessageAt);

// Everything the Telegram card on the Setting page shows: settings, live state of the poller and the chats.
public record TelegramStatusDto(
    bool Enabled,
    bool HasBotToken,
    string? BotTokenMasked,
    TelegramBotStatus State,
    string? BotUsername,
    string? Error,
    DateTime? LastPollAt,
    IReadOnlyList<TelegramChatDto> Chats);

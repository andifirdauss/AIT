namespace AIThread.Service.Telegram;

// The part of the Bot API objects AIThread reads. JSON names are the snake_case form (update_id, message_id, ...).
public sealed record TgUpdate(long UpdateId, TgMessage? Message, TgCallbackQuery? CallbackQuery);

// Date is Unix time in seconds (0 for a message that is too old to be accessible).
public sealed record TgMessage(long MessageId, TgUser? From, TgChat Chat, long Date, string? Text);

public sealed record TgChat(long Id, string Type, string? Title, string? Username, string? FirstName, string? LastName);

public sealed record TgUser(long Id, bool IsBot, string FirstName, string? LastName, string? Username);

public sealed record TgCallbackQuery(string Id, TgUser From, TgMessage? Message, string? Data);

public sealed record TgInlineKeyboard(IReadOnlyList<IReadOnlyList<TgInlineButton>> InlineKeyboard);

// CallbackData is limited to 64 bytes by Telegram.
public sealed record TgInlineButton(string Text, string CallbackData);

public sealed record TelegramCommand(string Command, string Description);

internal sealed record TgResponse<T>(bool Ok, T? Result, string? Description, int? ErrorCode, TgResponseParameters? Parameters);

internal sealed record TgResponseParameters(int? RetryAfter);

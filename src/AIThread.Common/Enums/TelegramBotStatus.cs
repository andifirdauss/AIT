namespace AIThread.Common.Enums;

// State of the Telegram long-polling loop, shown on the Setting page.
public enum TelegramBotStatus
{
    Disabled = 0,
    Starting = 1,
    Running = 2,
    Error = 3
}

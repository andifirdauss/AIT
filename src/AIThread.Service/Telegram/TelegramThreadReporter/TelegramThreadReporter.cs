using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Telegram;

public sealed record TelegramThreadContext(long ChatId, long PromptMessageId, long StatusMessageId, Guid ThreadId, string ProjectName);

// Keeps one Telegram message in sync with a running thread (status, task checklist, planner activity) and sends the
// answer as a new message when the thread ends: edits are silent, so the phone only notifies for the result.
// The database is the source of truth; bus events only say "something changed".
public sealed partial class TelegramThreadReporter(ITelegramApi api, IThreadService threadService, ILogger<TelegramThreadReporter> logger) : ITelegramThreadReporter
{
    // Telegram allows about one message per second per chat; status edits stay well below that.
    private static readonly TimeSpan MinEditInterval = TimeSpan.FromSeconds(3);

    // Refresh without events too (elapsed time, a dropped event).
    private static readonly TimeSpan IdleRefresh = TimeSpan.FromSeconds(15);

    private const int MaxResultMessages = 6;
}

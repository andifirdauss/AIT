using System.Collections.Concurrent;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Telegram;

// Turns Telegram updates into AIThread actions. Only private chats approved on the Setting page get through; for
// them a plain-text message runs a thread on the chat's active project, and commands manage the chat.
public sealed partial class TelegramBot(
    ITelegramApi api,
    ITelegramThreadReporter reporter,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IThreadOrchestrator orchestrator,
    IThreadService threadService,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IHostApplicationLifetime lifetime,
    ILogger<TelegramBot> logger) : ITelegramBot
{
    // Prompts sent while AIThread was not polling (laptop off, bot disabled) are not executed when it comes back.
    private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeniedReplyInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PendingPromptLifetime = TimeSpan.FromMinutes(10);

    // Earlier conversation sent to the planner: the same budget as for CLI chats (12 messages = 6 exchanges).
    private const int HistoryThreads = 6;
    private const int HistoryMessageChars = 1_500;
    private const int HistoryChars = 12_000;

    // Strangers who message the bot are listed for approval; only the most recent ones are kept.
    private const int MaxPendingChats = 20;
    private const int MaxDiffMessages = 8;

    private readonly ConcurrentDictionary<long, Guid> _running = new();
    private readonly ConcurrentDictionary<long, DateTime> _deniedAt = new();
    private readonly ConcurrentDictionary<long, PendingPrompt> _pending = new();

    private sealed record PendingPrompt(string Prompt, long MessageId, DateTime ReceivedAt);
}

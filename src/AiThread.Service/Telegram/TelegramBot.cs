using System.Collections.Concurrent;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

// Turns Telegram updates into AiThread actions. Only private chats approved on the Setting page get through; for
// them a plain-text message runs a thread on the chat's active project, and commands manage the chat.
public sealed class TelegramBot(
    TelegramApi api,
    TelegramThreadReporter reporter,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IThreadOrchestrator orchestrator,
    IThreadService threadService,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IHostApplicationLifetime lifetime,
    ILogger<TelegramBot> logger)
{
    // Prompts sent while AiThread was not polling (laptop off, bot disabled) are not executed when it comes back.
    private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeniedReplyInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PendingPromptLifetime = TimeSpan.FromMinutes(10);

    // Earlier conversation sent to the planner: the same budget as for Claude Code (12 messages = 6 exchanges).
    private const int HistoryThreads = 6;
    private const int HistoryMessageChars = 1_500;
    private const int HistoryChars = 12_000;

    // Strangers who message the bot are listed for approval; only the most recent ones are kept.
    private const int MaxPendingChats = 20;
    private const int MaxDiffMessages = 8;

    private readonly ConcurrentDictionary<long, Guid> _running = new();
    private readonly ConcurrentDictionary<long, DateTime> _deniedAt = new();
    private readonly ConcurrentDictionary<long, PendingPrompt> _pending = new();

    public static string SessionIdFor(long chatId) => $"telegram:{chatId}";

    public async Task HandleAsync(TgUpdate update, DateTime pollingStartedAt, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleButtonAsync(callback, cancellationToken);
            return;
        }

        if (update.Message is not { } message) return;
        if (message.Chat is not { Type: "private" })
        {
            // A group would let every member run prompts on this machine.
            logger.LogDebug("Ignoring Telegram message from non-private chat {ChatId}.", message.Chat?.Id);
            return;
        }

        var chat = await TouchChatAsync(message.Chat, cancellationToken);
        if (!chat.IsAllowed)
        {
            await ReplyNotAllowedAsync(chat, cancellationToken);
            return;
        }

        // Allowed now: if access is revoked later, the next message must be answered right away.
        _deniedAt.TryRemove(chat.ChatId, out _);

        try
        {
            await HandleAllowedMessageAsync(chat, message, pollingStartedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram message {MessageId} from chat {ChatId} failed.", message.MessageId, chat.ChatId);
            await TrySendAsync(chat.ChatId, TelegramMessages.Failure(ex.Message), cancellationToken);
        }
    }

    private async Task HandleAllowedMessageAsync(TelegramChat chat, TgMessage message, DateTime pollingStartedAt, CancellationToken cancellationToken)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.TextOnly, cancellationToken: cancellationToken);
            return;
        }

        if (text.StartsWith('/'))
        {
            await HandleCommandAsync(chat, text, cancellationToken);
            return;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(message.Date).UtcDateTime;
        if (sentAt < pollingStartedAt - StaleGrace)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.Stale(sentAt), message.MessageId, cancellationToken: cancellationToken);
            return;
        }

        await StartThreadAsync(chat, message.MessageId, text, cancellationToken);
    }

    private async Task HandleCommandAsync(TelegramChat chat, string text, CancellationToken cancellationToken)
    {
        var (command, argument) = TelegramMessages.ParseCommand(text);
        switch (command)
        {
            case "start":
            case "help":
                await api.SendAsync(chat.ChatId, TelegramMessages.Help(await ActiveProjectNameAsync(chat, cancellationToken)), cancellationToken: cancellationToken);
                break;
            case "projects":
            case "project":
                if (argument.Length == 0) await SendProjectMenuAsync(chat, null, cancellationToken);
                else await SelectProjectByNameAsync(chat, argument, cancellationToken);
                break;
            case "status":
                await SendStatusAsync(chat, cancellationToken);
                break;
            case "cancel":
                await CancelAsync(chat, argument, cancellationToken);
                break;
            case "diff":
                await SendDiffAsync(chat, argument, cancellationToken);
                break;
            case "new":
                await UpdateChatAsync(chat.ChatId, c => c.ConversationStartedAt = DateTime.UtcNow, cancellationToken);
                await api.SendAsync(chat.ChatId, TelegramMessages.ConversationReset, cancellationToken: cancellationToken);
                break;
            default:
                await api.SendAsync(chat.ChatId, TelegramMessages.UnknownCommand, cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleButtonAsync(TgCallbackQuery callback, CancellationToken cancellationToken)
    {
        var chat = await FindChatAsync(callback.Message?.Chat?.Id ?? callback.From.Id, cancellationToken);
        if (chat is not { IsAllowed: true })
        {
            await api.AnswerCallbackAsync(callback.Id, "Chat ini belum diizinkan.", cancellationToken);
            return;
        }

        try
        {
            if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.ProjectButton, out var projectId))
                await SelectProjectFromButtonAsync(chat, callback, projectId, cancellationToken);
            else if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.CancelButton, out var threadId))
                await api.AnswerCallbackAsync(callback.Id, threadService.Cancel(threadId) ? "Pembatalan dikirim." : "Thread sudah selesai.", cancellationToken);
            else
                await api.AnswerCallbackAsync(callback.Id, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram button {Data} from chat {ChatId} failed.", callback.Data, chat.ChatId);
            await TrySendAsync(chat.ChatId, TelegramMessages.Failure(ex.Message), cancellationToken);
        }
    }

    private async Task StartThreadAsync(TelegramChat chat, long promptMessageId, string prompt, CancellationToken cancellationToken)
    {
        // One thread per chat at a time: a second prompt would race the first one on the same files.
        if (_running.TryGetValue(chat.ChatId, out var runningId))
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.StillRunning(runningId), promptMessageId, cancellationToken: cancellationToken);
            return;
        }

        var project = await ResolveProjectAsync(chat, cancellationToken);
        if (project is null)
        {
            _pending[chat.ChatId] = new PendingPrompt(prompt, promptMessageId, DateTime.UtcNow);
            await SendProjectMenuAsync(chat, TelegramMessages.PickProjectForPrompt, cancellationToken);
            return;
        }

        var conversation = await BuildConversationAsync(chat, project.Id, cancellationToken);
        var statusMessageId = await api.SendAsync(chat.ChatId, TelegramMessages.Starting(project.Name), promptMessageId, cancellationToken: cancellationToken);

        // Subscribe before starting: the first events fire before StartInBackgroundAsync returns the thread id.
        var subscription = eventBus.Subscribe(evt => evt.ThreadId is not null &&
                                                     evt.Type is AppEventTypes.ThreadUpdated or AppEventTypes.TaskUpdated or AppEventTypes.ThreadOutput);
        Guid threadId;
        try
        {
            threadId = await orchestrator.StartInBackgroundAsync(new ThreadRequest(
                ThreadSource.Telegram,
                prompt,
                PromptContext: null,
                conversation,
                WorkingDirectory: null,
                project.Id,
                SessionIdFor(chat.ChatId)), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            subscription.Dispose();
            logger.LogWarning(ex, "Telegram prompt from chat {ChatId} could not start a thread.", chat.ChatId);
            await api.EditAsync(chat.ChatId, statusMessageId, TelegramMessages.StartFailed(ex.Message), cancellationToken: cancellationToken);
            return;
        }

        _running[chat.ChatId] = threadId;
        var context = new TelegramThreadContext(chat.ChatId, promptMessageId, statusMessageId, threadId, project.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await reporter.RunAsync(context, subscription, lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
                await NotifyInterruptedAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reporting thread {ThreadId} to Telegram failed.", threadId);
            }
            finally
            {
                subscription.Dispose();
                _running.TryRemove(new KeyValuePair<long, Guid>(chat.ChatId, threadId));
            }
        }, CancellationToken.None);
    }

    // AiThread is stopping mid-thread: without this the chat would wait for an answer that never comes.
    // Skipped when the thread had already finished and only sending its result was cut short.
    private async Task NotifyInterruptedAsync(TelegramThreadContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var status = (await threadService.GetDetailAsync(context.ThreadId, timeout.Token))?.Summary.Status;
            if (status is ThreadStatus.Completed or ThreadStatus.Failed) return;
            await api.SendAsync(context.ChatId, TelegramMessages.Interrupted(context.ThreadId), context.PromptMessageId, cancellationToken: timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not tell Telegram chat {ChatId} that thread {ThreadId} was interrupted.", context.ChatId, context.ThreadId);
        }
    }

    private async Task SendProjectMenuAsync(TelegramChat chat, string? intro, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (projects.Count == 0)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.NoProjects, cancellationToken: cancellationToken);
            return;
        }

        await api.SendAsync(chat.ChatId, TelegramMessages.ProjectList(projects, chat.ActiveProjectId, intro),
            keyboard: TelegramMessages.ProjectKeyboard(projects, chat.ActiveProjectId), cancellationToken: cancellationToken);
    }

    // "/project 2" (number from the list) or "/project name" (exact, else a unique partial match).
    private async Task SelectProjectByNameAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        var project = int.TryParse(argument, out var number) && number >= 1 && number <= projects.Count
            ? projects[number - 1]
            : projects.FirstOrDefault(p => string.Equals(p.Name, argument, StringComparison.OrdinalIgnoreCase))
              ?? (projects.Where(p => p.Name.Contains(argument, StringComparison.OrdinalIgnoreCase)).ToList() is [var single] ? single : null);
        if (project is null)
        {
            await SendProjectMenuAsync(chat, TelegramMessages.ProjectNotFound(argument), cancellationToken);
            return;
        }

        await SetActiveProjectAsync(chat, project, cancellationToken);
        var pending = TakePendingPrompt(chat.ChatId);
        await api.SendAsync(chat.ChatId, TelegramMessages.ProjectSelected(project.Name, pending is not null), cancellationToken: cancellationToken);
        if (pending is not null) await StartThreadAsync(chat, pending.MessageId, pending.Prompt, cancellationToken);
    }

    private async Task SelectProjectFromButtonAsync(TelegramChat chat, TgCallbackQuery callback, Guid projectId, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (projects.FirstOrDefault(p => p.Id == projectId) is not { } project)
        {
            await api.AnswerCallbackAsync(callback.Id, "Project sudah tidak terdaftar.", cancellationToken);
            return;
        }

        await SetActiveProjectAsync(chat, project, cancellationToken);
        await api.AnswerCallbackAsync(callback.Id, TelegramFormatter.Truncate($"Project aktif: {project.Name}", 200), cancellationToken);

        var pending = TakePendingPrompt(chat.ChatId);
        if (callback.Message is { } menu)
            await api.EditAsync(chat.ChatId, menu.MessageId,
                TelegramMessages.ProjectList(projects, project.Id, TelegramMessages.ProjectSelected(project.Name, pending is not null)),
                TelegramMessages.ProjectKeyboard(projects, project.Id), cancellationToken);
        if (pending is not null) await StartThreadAsync(chat, pending.MessageId, pending.Prompt, cancellationToken);
    }

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

    private async Task SendStatusAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var running = _running.TryGetValue(chat.ChatId, out var runningId)
            ? (await threadService.GetDetailAsync(runningId, cancellationToken))?.Summary
            : null;
        var recent = await threadService.GetRecentAsync(5, null, cancellationToken);
        await api.SendAsync(chat.ChatId,
            TelegramMessages.Status(await ActiveProjectNameAsync(chat, cancellationToken), running, recent, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    // No argument: this chat's running thread. With an id prefix: any running thread, e.g. one from Claude Code.
    private async Task CancelAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        if (argument.Length == 0)
        {
            var reply = _running.TryGetValue(chat.ChatId, out var id)
                ? threadService.Cancel(id) ? TelegramMessages.CancelSent(id) : TelegramMessages.NotRunning(id)
                : TelegramMessages.NothingToCancel;
            await api.SendAsync(chat.ChatId, reply, cancellationToken: cancellationToken);
            return;
        }

        var match = await FindThreadAsync(argument, activeOnly: true, cancellationToken);
        await api.SendAsync(chat.ChatId,
            match is { } threadId && threadService.Cancel(threadId) ? TelegramMessages.CancelSent(threadId) : TelegramMessages.NoRunningMatch(argument),
            cancellationToken: cancellationToken);
    }

    private async Task SendDiffAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        var threadId = argument.Length > 0
            ? await FindThreadAsync(argument, activeOnly: false, cancellationToken)
            : await LastChatThreadAsync(chat.ChatId, cancellationToken);
        var detail = threadId is { } id ? await threadService.GetDetailAsync(id, cancellationToken) : null;
        if (detail is null)
        {
            await api.SendAsync(chat.ChatId, argument.Length > 0 ? TelegramMessages.ThreadNotFound(argument) : TelegramMessages.NoThreadYet,
                cancellationToken: cancellationToken);
            return;
        }

        var changed = detail.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.Diff)).ToList();
        if (changed.Count == 0)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.NoChanges(detail.Summary.Id), cancellationToken: cancellationToken);
            return;
        }

        var blocks = new List<string> { TelegramMessages.DiffHeader(detail.Summary.Id, changed.Count) };
        foreach (var task in changed)
        {
            blocks.Add(string.Empty);
            blocks.Add(TelegramMessages.DiffTask(task));
            blocks.AddRange(TelegramFormatter.CodeBlocks(task.Diff!.ReplaceLineEndings("\n").Split('\n'), "diff"));
        }

        var chunks = TelegramFormatter.Pack(blocks);
        foreach (var chunk in chunks.Take(MaxDiffMessages)) await api.SendAsync(chat.ChatId, chunk, cancellationToken: cancellationToken);
        if (chunks.Count > MaxDiffMessages) await api.SendAsync(chat.ChatId, TelegramMessages.DiffTruncated, cancellationToken: cancellationToken);
    }

    // Finds or registers the chat and refreshes its name; a new chat waits for approval on the Setting page.
    private async Task<TelegramChat> TouchChatAsync(TgChat source, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == source.Id, cancellationToken);
        var isNew = chat is null;
        if (chat is null)
        {
            var oldPending = await db.TelegramChats
                .Where(c => !c.IsAllowed)
                .OrderByDescending(c => c.LastMessageAt)
                .Skip(MaxPendingChats - 1)
                .ToListAsync(cancellationToken);
            db.TelegramChats.RemoveRange(oldPending);

            chat = new TelegramChat { ChatId = source.Id };
            db.TelegramChats.Add(chat);
        }
        else
        {
            chat.UpdatedAt = DateTime.UtcNow;
        }

        chat.DisplayName = TelegramFormatter.Truncate(DisplayName(source), 200);
        chat.Username = source.Username is { } username ? TelegramFormatter.Truncate(username, 100) : null;
        chat.LastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (isNew)
        {
            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "created" }));
            activityLog.Warn("telegram",
                $"Chat Telegram baru menunggu izin: {chat.DisplayName}{(chat.Username is null ? "" : $" (@{chat.Username})")}, ID {chat.ChatId}. Izinkan di menu Setting bila ini Anda.");
        }
        return chat;
    }

    private async Task ReplyNotAllowedAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_deniedAt.TryGetValue(chat.ChatId, out var last) && now - last < DeniedReplyInterval) return;
        _deniedAt[chat.ChatId] = now;
        await TrySendAsync(chat.ChatId, TelegramMessages.NotAllowed(chat.DisplayName, chat.ChatId), cancellationToken);
    }

    private async Task<TelegramChat?> FindChatAsync(long chatId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TelegramChats.AsNoTracking().FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
    }

    private async Task UpdateChatAsync(long chatId, Action<TelegramChat> mutate, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (chat is null) return;

        mutate(chat);
        chat.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // Sorted by name so the numbers in /projects stay stable.
    private async Task<List<TelegramProject>> ProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking()
            .OrderBy(p => p.Name).ThenBy(p => p.RootPath)
            .Select(p => new TelegramProject(p.Id, p.Name, p.RootPath))
            .ToListAsync(cancellationToken);
    }

    // The chat's active project; when none is set and exactly one project is registered, that one.
    private async Task<TelegramProject?> ResolveProjectAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(cancellationToken);
        if (chat.ActiveProjectId is { } id && projects.FirstOrDefault(p => p.Id == id) is { } active) return active;
        if (projects.Count != 1) return null;

        await SetActiveProjectAsync(chat, projects[0], cancellationToken);
        return projects[0];
    }

    private async Task<string?> ActiveProjectNameAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        if (chat.ActiveProjectId is not { } id) return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
    }

    // Earlier prompts and answers of this chat in this project since the last /new, formatted like the Claude Code
    // history the planner already knows. Model-facing text is English.
    private async Task<string?> BuildConversationAsync(TelegramChat chat, Guid projectId, CancellationToken cancellationToken)
    {
        var sessionId = SessionIdFor(chat.ChatId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.ProjectId == projectId && t.CreatedAt >= chat.ConversationStartedAt && t.CompletedAt != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(HistoryThreads)
            .Select(t => new { t.Prompt, t.FinalResponse, t.Error, t.Status })
            .ToListAsync(cancellationToken);
        if (threads.Count == 0) return null;

        var parts = new List<string>();
        foreach (var thread in Enumerable.Reverse(threads))
        {
            var answer = thread.FinalResponse
                         ?? (thread.Status == ThreadStatus.Cancelled ? "(the thread was cancelled)" : $"(the thread failed: {thread.Error})");
            parts.Add($"[user] {Clip(thread.Prompt)}");
            parts.Add($"[assistant] {Clip(answer)}");
        }

        var history = string.Join("\n\n", parts);
        return history.Length <= HistoryChars ? history : "…" + history[^HistoryChars..];

        static string Clip(string text) => text.Length > HistoryMessageChars ? text[..HistoryMessageChars] + " …" : text;
    }

    private async Task<Guid?> LastChatThreadAsync(long chatId, CancellationToken cancellationToken)
    {
        var sessionId = SessionIdFor(chatId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Threads.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // By the id prefix shown everywhere (8 characters, at least 4), newest first.
    private async Task<Guid?> FindThreadAsync(string prefix, bool activeOnly, CancellationToken cancellationToken)
    {
        var normalized = prefix.Trim();
        if (normalized.Length < 4) return null;

        var recent = await threadService.GetRecentAsync(200, null, cancellationToken);
        return recent.FirstOrDefault(t => (!activeOnly || TelegramMessages.IsActive(t.Status)) &&
                                          t.Id.ToString().StartsWith(normalized, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private PendingPrompt? TakePendingPrompt(long chatId) =>
        _pending.TryRemove(chatId, out var pending) && DateTime.UtcNow - pending.ReceivedAt <= PendingPromptLifetime ? pending : null;

    private async Task TrySendAsync(long chatId, string html, CancellationToken cancellationToken)
    {
        try
        {
            await api.SendAsync(chatId, html, cancellationToken: cancellationToken);
        }
        catch (TelegramApiException ex)
        {
            logger.LogDebug(ex, "Could not send a Telegram message to chat {ChatId}.", chatId);
        }
    }

    private static string DisplayName(TgChat chat)
    {
        var name = string.Join(' ', new[] { chat.FirstName, chat.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (name.Length > 0) return name;
        return chat.Title ?? (chat.Username is { } username ? "@" + username : chat.Id.ToString());
    }

    private sealed record PendingPrompt(string Prompt, long MessageId, DateTime ReceivedAt);
}

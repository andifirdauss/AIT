using System.Text.Json;
using AiThread.Common.DTOs;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed record TelegramThreadContext(long ChatId, long PromptMessageId, long StatusMessageId, Guid ThreadId, string ProjectName);

// Keeps one Telegram message in sync with a running thread (status, task checklist, planner activity) and sends the
// answer as a new message when the thread ends: edits are silent, so the phone only notifies for the result.
// The database is the source of truth; bus events only say "something changed".
public sealed class TelegramThreadReporter(TelegramApi api, IThreadService threadService, ILogger<TelegramThreadReporter> logger)
{
    // Telegram allows about one message per second per chat; status edits stay well below that.
    private static readonly TimeSpan MinEditInterval = TimeSpan.FromSeconds(3);

    // Refresh without events too (elapsed time, a dropped event).
    private static readonly TimeSpan IdleRefresh = TimeSpan.FromSeconds(15);

    private const int MaxResultMessages = 6;

    public async Task RunAsync(TelegramThreadContext context, IEventSubscription subscription, CancellationToken cancellationToken)
    {
        long? statusMessageId = context.StatusMessageId;
        string? plannerActivity = null;
        string? shown = null;
        var nextEditAt = DateTime.MinValue;

        while (true)
        {
            plannerActivity = await WaitForChangeAsync(subscription, context.ThreadId, plannerActivity, cancellationToken);

            var wait = nextEditAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
                plannerActivity = Drain(subscription, context.ThreadId, plannerActivity).Activity;
            }

            var detail = await threadService.GetDetailAsync(context.ThreadId, cancellationToken);
            if (detail is null) return;
            var finished = !TelegramMessages.IsActive(detail.Summary.Status);

            if (statusMessageId is { } messageId)
            {
                var text = TelegramMessages.ThreadProgress(detail, context.ProjectName, finished ? null : plannerActivity, DateTime.UtcNow);
                if (text != shown)
                {
                    try
                    {
                        await api.EditAsync(context.ChatId, messageId, text, finished ? null : TelegramMessages.CancelKeyboard(context.ThreadId), cancellationToken);
                        shown = text;
                    }
                    catch (TelegramApiException ex) when (ex.IsMessageGone)
                    {
                        // Deleted by the user; the answer still arrives as a new message.
                        statusMessageId = null;
                    }
                    catch (TelegramApiException ex) when (ex.IsChatUnreachable)
                    {
                        logger.LogInformation("Telegram chat {ChatId} is unreachable; stopped reporting thread {ThreadId}.", context.ChatId, context.ThreadId);
                        return;
                    }
                    catch (TelegramApiException ex)
                    {
                        logger.LogDebug(ex, "Telegram status update for thread {ThreadId} failed.", context.ThreadId);
                    }
                    nextEditAt = DateTime.UtcNow + MinEditInterval;
                }
            }

            if (finished)
            {
                await SendResultAsync(context, detail, cancellationToken);
                return;
            }
        }
    }

    private async Task SendResultAsync(TelegramThreadContext context, ThreadDetailDto detail, CancellationToken cancellationToken)
    {
        var blocks = new List<string> { TelegramMessages.ResultHeader(detail, context.ProjectName) };
        if (!string.IsNullOrWhiteSpace(detail.FinalResponse))
        {
            blocks.Add(string.Empty);
            blocks.AddRange(TelegramFormatter.ToBlocks(detail.FinalResponse));
        }
        if (detail.Tasks.Any(t => !string.IsNullOrWhiteSpace(t.Diff)))
        {
            blocks.Add(string.Empty);
            blocks.Add("<i>Kirim /diff untuk melihat perubahan file.</i>");
        }

        var chunks = TelegramFormatter.Pack(blocks);
        long? replyTo = context.PromptMessageId;
        foreach (var chunk in chunks.Take(MaxResultMessages))
        {
            await api.SendAsync(context.ChatId, chunk, replyTo, cancellationToken: cancellationToken);
            replyTo = null;
        }
        if (chunks.Count > MaxResultMessages)
            await api.SendAsync(context.ChatId,
                $"… {chunks.Count - MaxResultMessages} bagian lagi tidak dikirim. Lihat detail thread di dashboard AiThread.", cancellationToken: cancellationToken);
    }

    // Returns when an event about this thread arrives or after the idle refresh, keeping the latest planner activity.
    private static async Task<string?> WaitForChangeAsync(IEventSubscription subscription, Guid threadId, string? activity, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(IdleRefresh);
        try
        {
            while (await subscription.Reader.WaitToReadAsync(idle.Token))
            {
                (var changed, activity) = Drain(subscription, threadId, activity);
                if (changed) return activity;
            }

            // The subscription was closed: fall back to polling the database.
            await Task.Delay(IdleRefresh, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        return activity;
    }

    private static (bool Changed, string? Activity) Drain(IEventSubscription subscription, Guid threadId, string? activity)
    {
        var changed = false;
        while (subscription.Reader.TryRead(out var evt))
        {
            if (evt.ThreadId != threadId) continue;
            changed = true;
            if (evt.Type == AppEventTypes.ThreadOutput && DescribePlannerActivity(evt.Data) is { } described) activity = described;
        }
        return (changed, activity);
    }

    // thread.output events carry { kind = "planner.tool", tool, detail }.
    private static string? DescribePlannerActivity(object? data)
    {
        if (data is null) return null;
        try
        {
            var json = JsonSerializer.SerializeToElement(data);
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("tool", out var tool)) return null;
            var detail = json.TryGetProperty("detail", out var value) ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(detail) ? tool.GetString() : $"{tool.GetString()} · {detail}";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}

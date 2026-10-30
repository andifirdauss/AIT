using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using Spectre.Console;

namespace AIThread.UI.Hosting.Chat;

// Detail: the thread when it reached a final status (null when the developer detached with Esc).
public sealed record FollowResult(ThreadDetailDto? Detail, bool Detached);

// Follows one thread until it ends: the SSE feed of the thread drives the progress, and GET /api/threads/{id} is read
// at the start and every few seconds, so events missed before the feed opened (or a dropped feed) never leave the chat
// waiting. Interactive: Ctrl+C twice cancels the thread, Esc detaches, v shows the task output.
public sealed class ThreadFollower(ChatApiClient api, ChatRenderer renderer)
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CancelWindow = TimeSpan.FromSeconds(3);

    // Set from outside (Ctrl+C under -p): cancel the thread once and keep following until it stops.
    public volatile bool CancelRequested;

    // restarted: the thread had a final status and runs again (retry); its old status does not end the follow until
    // the thread moved, or after a short wait.
    public async Task<FollowResult> FollowAsync(Guid threadId, bool interactive, bool restarted, CancellationToken cancellationToken)
    {
        var state = new ChatThreadState(threadId);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = Channel.CreateUnbounded<AppEvent>();
        var feed = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in api.StreamAsync(threadId, stop.Token)) events.Writer.TryWrite(evt);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChatApiException or HttpRequestException or IOException) { }
            finally
            {
                events.Writer.TryComplete();
            }
        }, CancellationToken.None);

        try
        {
            return renderer.Rich
                ? await FollowLiveAsync(state, interactive, restarted, events.Reader, stop.Token)
                : await LoopAsync(state, interactive, restarted, events.Reader, () => { }, stop.Token);
        }
        finally
        {
            await stop.CancelAsync();
            await feed;
        }
    }

    private async Task<FollowResult> FollowLiveAsync(ChatThreadState state, bool interactive, bool restarted, ChannelReader<AppEvent> events, CancellationToken cancellationToken)
    {
        // The live area keeps the height of its tallest draw, so the block folded at the end would leave the rows of
        // the running stages blank below it: the area is cleared and the folded block written once after it.
        FollowResult? result = null;
        await AnsiConsole.Live(renderer.Progress(state, interactive))
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async context =>
            {
                result = await LoopAsync(state, interactive, restarted, events, () =>
                {
                    context.UpdateTarget(renderer.Progress(state, interactive));
                    context.Refresh();
                }, cancellationToken);
            });
        state.Notice = null;
        AnsiConsole.Write(renderer.Progress(state, interactive: false));
        return result!;
    }

    private async Task<FollowResult> LoopAsync(ChatThreadState state, bool interactive, bool restarted, ChannelReader<AppEvent> events, Action draw,
        CancellationToken cancellationToken)
    {
        var treatControlC = interactive && Console.TreatControlCAsInput;
        if (interactive) Console.TreatControlCAsInput = true;
        try
        {
            var poll = Stopwatch.StartNew();
            var started = Stopwatch.StartNew();
            DateTime? cancelArmed = null;
            var cancelSent = false;

            await Snapshot(state, cancellationToken);
            while (true)
            {
                while (events.TryRead(out var evt))
                    if (state.Apply(evt) is { } change && !renderer.Rich) renderer.Plain(change);

                // A retried thread still shows its old final status until the retry starts it again.
                var waitingForRestart = restarted && !state.Moved && started.Elapsed < TimeSpan.FromSeconds(15);
                if (state.IsFinal && !waitingForRestart)
                {
                    // The final status can come before the last task events; one read settles the details.
                    var detail = await api.GetThreadAsync(state.ThreadId, cancellationToken);
                    state.Apply(detail);
                    draw();
                    return new FollowResult(detail, Detached: false);
                }

                if (CancelRequested && !cancelSent)
                {
                    cancelSent = true;
                    await CancelAsync(state, cancellationToken);
                }

                if (interactive)
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        {
                            if (cancelArmed is { } armed && DateTime.UtcNow - armed < CancelWindow)
                            {
                                cancelArmed = null;
                                await CancelAsync(state, cancellationToken);
                            }
                            else
                            {
                                cancelArmed = DateTime.UtcNow;
                                state.Notice = Lang.T("Tekan Ctrl+C sekali lagi untuk membatalkan thread.", "Press Ctrl+C once more to cancel the thread.");
                            }
                        }
                        else if (key.Key == ConsoleKey.Escape)
                        {
                            draw();
                            return new FollowResult(null, Detached: true);
                        }
                        else if (key.KeyChar is 'v' or 'V')
                        {
                            renderer.Verbose = !renderer.Verbose;
                            state.Notice = renderer.Verbose ? Lang.T("Output tugas ditampilkan.", "Task output shown.") : Lang.T("Output tugas disembunyikan.", "Task output hidden.");
                        }
                    }

                if (cancelArmed is { } since && DateTime.UtcNow - since >= CancelWindow && state.Notice is not null)
                {
                    cancelArmed = null;
                    state.Notice = null;
                }

                if (poll.Elapsed >= PollEvery || events.Completion.IsCompleted && poll.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    poll.Restart();
                    await Snapshot(state, cancellationToken);
                }

                draw();
                await Task.Delay(renderer.Rich ? 100 : 200, cancellationToken);
            }
        }
        finally
        {
            if (interactive) Console.TreatControlCAsInput = treatControlC;
        }
    }

    // What the ended thread waits for the developer to allow, or null. A paused planner is recorded as AwaitingInput
    // just before its request is registered; a commit/push is asked after the thread completed and its memory was
    // written, so a plan with "publish" is waited for a while (Esc stops waiting when interactive).
    public async Task<PendingApprovalDto?> PendingApprovalAsync(ThreadDetailDto detail, bool interactive, CancellationToken cancellationToken)
    {
        var publish = detail.Summary.Status == ThreadStatus.Completed && PlanAsksPublish(detail.PlanJson);
        var wait = detail.Summary.Status == ThreadStatus.AwaitingInput ? TimeSpan.FromSeconds(3) : publish ? TimeSpan.FromSeconds(60) : TimeSpan.Zero;
        if (publish) renderer.Info(Lang.T("Menunggu langkah commit/push ... (Esc lewati)", "Waiting for the commit/push step ... (Esc to skip)"));

        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (await api.GetApprovalAsync(detail.Summary.Id, cancellationToken) is { } approval) return approval;
            if (clock.Elapsed >= wait) return null;
            if (interactive && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape) return null;
            await Task.Delay(publish ? 1000 : 300, cancellationToken);
        }
    }

    // The text a commit/push added at the end of the answer.
    public static string? Added(string? before, string? after)
    {
        if (string.IsNullOrWhiteSpace(after)) return null;
        if (string.IsNullOrEmpty(before)) return after.Trim();
        return after.StartsWith(before, StringComparison.Ordinal) ? after[before.Length..].Trim() : after.Trim();
    }

    private static bool PlanAsksPublish(string? planJson)
    {
        if (string.IsNullOrWhiteSpace(planJson)) return false;
        try
        {
            using var plan = JsonDocument.Parse(planJson);
            return plan.RootElement.ValueKind == JsonValueKind.Object
                   && plan.RootElement.EnumerateObject().Any(p => p.Name.Equals("publish", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task Snapshot(ChatThreadState state, CancellationToken cancellationToken)
    {
        try
        {
            if (state.Apply(await api.GetThreadAsync(state.ThreadId, cancellationToken)) is { } change && !renderer.Rich) renderer.Plain(change);
        }
        catch (ChatApiException ex)
        {
            state.Notice = ex.Message;
            if (!renderer.Rich) renderer.Error(ex.Message);
        }
    }

    private async Task CancelAsync(ChatThreadState state, CancellationToken cancellationToken)
    {
        try
        {
            await api.CancelAsync(state.ThreadId, cancellationToken);
            state.Notice = Lang.T("Pembatalan dikirim; menunggu thread berhenti ...", "Cancellation sent; waiting for the thread to stop ...");
        }
        catch (ChatApiException ex)
        {
            state.Notice = ex.Message;
        }
        if (!renderer.Rich) renderer.Info(state.Notice);
    }
}

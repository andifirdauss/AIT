using AIThread.Service.CliBackend;
using AIThread.Service.Models;

namespace AIThread.Tests;

public class CliSlotsTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static NineRouterException NotInstalled(Exception ex) => new(ex.Message, 500);

    // The planner keeps planning while its committed tasks run: its turn takes the next free slot before the executors.
    [Fact]
    public async Task A_priority_call_gets_the_next_free_slot_before_earlier_waiters()
    {
        var slots = new CliSlots(total: 1, background: 1);
        var busy = await slots.AcquireAsync(CancellationToken.None);
        var executor = slots.AcquireAsync(CancellationToken.None);
        Task<IDisposable> planner;
        using (CliSlots.Priority())
            planner = slots.AcquireAsync(CancellationToken.None);

        busy.Dispose();
        var first = await planner.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(executor.IsCompleted);
        first.Dispose();
        (await executor.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.False(CliSlots.IsPriority);
    }

    [Fact]
    public async Task A_cancelled_waiter_leaves_the_queue_and_the_slot_goes_to_the_next()
    {
        var slots = new CliSlots(total: 1, background: 1);
        var busy = await slots.AcquireAsync(CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        Task<IDisposable> gone;
        using (CliSlots.Priority())
            gone = slots.AcquireAsync(cancel.Token);
        var next = slots.AcquireAsync(CancellationToken.None);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gone);
        busy.Dispose();
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Background_work_keeps_the_other_slots_free_for_threads()
    {
        var slots = new CliSlots(total: 3, background: 1);
        IDisposable first;
        Task<IDisposable> second;
        using (CliSlots.Background())
        {
            first = await slots.AcquireAsync(CancellationToken.None);
            second = slots.AcquireAsync(CancellationToken.None);
        }

        // The second background call waits, while two thread calls still get a slot at once.
        Assert.False(second.IsCompleted);
        using var planner = await slots.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        using var executor = await slots.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        first.Dispose();
        using var next = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(CliSlots.IsBackground);
    }

    [Fact]
    public async Task Background_scope_flows_into_awaited_work_and_ends_with_it()
    {
        bool inside;
        using (CliSlots.Background())
            inside = await Task.Run(() => CliSlots.IsBackground);

        Assert.True(inside);
        Assert.False(CliSlots.IsBackground);
    }

    [Fact]
    public async Task A_single_slot_is_shared_by_background_work_and_threads()
    {
        var slots = new CliSlots(total: 1, background: 1);
        Task<IDisposable> background;
        using (CliSlots.Background())
            background = slots.AcquireAsync(CancellationToken.None);
        using (await background.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            var thread = slots.AcquireAsync(CancellationToken.None);
            Assert.False(thread.IsCompleted);
        }
    }

    [Fact]
    public async Task A_cancelled_background_wait_gives_its_background_slot_back()
    {
        var slots = new CliSlots(total: 2, background: 1);
        using var thread = await slots.AcquireAsync(CancellationToken.None);
        using var busy = await slots.AcquireAsync(CancellationToken.None);
        using (CliSlots.Background())
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slots.AcquireAsync(cancel.Token));
        }

        busy.Dispose();
        using (CliSlots.Background())
        using (await slots.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)))
        {
        }
    }

    // A CLI that keeps writing past the call's time limit: a long answer, not a dropped connection.
    [Fact]
    public async Task Cli_still_writing_at_the_time_limit_is_a_time_limit_failure()
    {
        var ex = await Assert.ThrowsAsync<NineRouterException>(() => CliProcess.RunAsync(
            new CliLaunch("cmd.exe", ["/c", "for /l %i in (1,0,2) do @echo x"]), [], _workspace.Root, null, _ => { },
            _ => Task.CompletedTask, new CliCallLimits(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)), "Fake", NotInstalled, CancellationToken.None));

        Assert.Equal(408, ex.StatusCode);
        Assert.True(ex.IsTimeLimit);
        Assert.False(ex.IsTransient);
        Assert.True(ex.IsModelUnavailable);
    }

    [Fact]
    public async Task Silent_cli_is_a_stall_that_may_be_retried()
    {
        var ex = await Assert.ThrowsAsync<NineRouterException>(() => CliProcess.RunAsync(
            new CliLaunch("cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"]), [], _workspace.Root, null, _ => { },
            _ => Task.CompletedTask, new CliCallLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)), "Fake", NotInstalled, CancellationToken.None));

        Assert.Equal(408, ex.StatusCode);
        Assert.False(ex.IsTimeLimit);
        Assert.True(ex.IsTransient);
    }
}

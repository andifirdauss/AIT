using System.Diagnostics;
using AIThread.Service.CliBackend;
using AIThread.Service.Models;

namespace AIThread.Tests;

// With real processes: `cmd /c more` is a program that waits on stdin, like a CLI waiting for its prompt.
public class CliWarmPoolTests : IDisposable
{
    private static readonly CliCallLimits Limits = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static NineRouterException NotInstalled(Exception ex) => new(ex.Message, 500);

    private WarmCliProcess Waiting() =>
        new(CliProcess.Start(new CliLaunch("cmd.exe", ["/c", "more"]), [], _workspace.Root, _ => { }, NotInstalled), Directory.CreateTempSubdirectory("warm").FullName);

    private static CliWarmPool Pool(int capacity = 2, int idleMs = 60_000) => new(capacity, TimeSpan.FromMilliseconds(idleMs), CliProcess.Discard);

    [Fact]
    public void A_warm_process_is_handed_out_once()
    {
        using var pool = Pool();
        var warm = Waiting();
        pool.Add("a", warm);

        Assert.True(pool.Has("a"));
        Assert.Same(warm, pool.Take("a"));
        Assert.Null(pool.Take("a"));
        CliProcess.Discard(warm);
    }

    [Fact]
    public void The_oldest_goes_when_the_pool_is_full()
    {
        using var pool = Pool(capacity: 1);
        var (first, second) = (Waiting(), Waiting());

        pool.Add("a", first);
        pool.Add("b", second);

        Assert.False(pool.Has("a"));
        Assert.True(pool.Has("b"));
        Assert.False(Directory.Exists(first.WorkFolder));
    }

    [Fact]
    public async Task One_nobody_takes_stops_by_itself()
    {
        using var pool = Pool(idleMs: 100);
        var warm = Waiting();
        pool.Add("a", warm);

        for (var i = 0; i < 100 && Directory.Exists(warm.WorkFolder); i++) await Task.Delay(50);

        Assert.False(pool.Has("a"));
        Assert.False(Directory.Exists(warm.WorkFolder));
    }

    [Fact]
    public void Closing_the_pool_stops_what_waits_and_refuses_new_ones()
    {
        var pool = Pool();
        var (waiting, late) = (Waiting(), Waiting());
        pool.Add("a", waiting);

        pool.Dispose();
        pool.Add("b", late);

        Assert.False(Directory.Exists(waiting.WorkFolder));
        Assert.False(Directory.Exists(late.WorkFolder));
        Assert.Null(pool.Take("b"));
    }

    [Fact]
    public void Without_capacity_nothing_is_kept()
    {
        using var pool = Pool(capacity: 0);
        var warm = Waiting();

        pool.Add("a", warm);

        Assert.False(pool.Has("a"));
        Assert.False(Directory.Exists(warm.WorkFolder));
    }

    [Fact]
    public async Task A_started_process_gets_its_input_when_it_is_used()
    {
        var lines = new List<string>();

        var exit = await CliProcess.RunStartedAsync(Waiting().Process, "pong\n", line => { lines.Add(line); return Task.CompletedTask; }, Limits, "Test", CancellationToken.None);

        Assert.Equal(0, exit.ExitCode);
        Assert.Contains("pong", lines);
    }

    [Fact]
    public async Task A_finished_answer_does_not_wait_for_the_process_to_end()
    {
        var clock = Stopwatch.StartNew();
        var lines = new List<string>();

        // Prints the answer, then keeps running for about 20 seconds.
        var exit = await CliProcess.RunAsync(new CliLaunch("cmd.exe", ["/c", "echo answer& ping -n 21 127.0.0.1 >nul"]), [], _workspace.Root, null, _ => { },
            line => { lines.Add(line.Trim()); return Task.CompletedTask; }, Limits, "Test", NotInstalled, CancellationToken.None, () => lines.Contains("answer"));

        Assert.Equal(0, exit.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }
}

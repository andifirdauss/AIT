using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// start_process / process_output / stop_process: a dev server or watcher the planner keeps running while it investigates.
// The commands are bash; a machine without Git Bash (cmd.exe fallback) skips the checks.
public class BackgroundProcessTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly BackgroundProcesses _processes = new(NullLogger<BackgroundProcesses>.Instance);
    private readonly Guid _thread = Guid.NewGuid();

    public void Dispose()
    {
        _processes.Dispose();
        _workspace.Dispose();
    }

    [Fact]
    public async Task A_process_runs_until_its_text_shows_and_stops_with_its_output()
    {
        var started = await _processes.StartAsync(_thread, "echo booting; echo ready-marker; sleep 30", _workspace.Root, new BackgroundWait(null, "READY-marker", 20));
        if (started.Shell == "cmd.exe") return;

        Assert.NotNull(started.Id);
        Assert.True(started.Ready);
        Assert.False(started.Exited);
        Assert.Contains("booting", started.Output);
        Assert.Equal(1, _processes.Running(_thread));

        var output = _processes.Output(_thread, started.Id!, 1);
        Assert.NotNull(output);
        Assert.True(output!.Running);
        Assert.Equal("ready-marker", output.Output);
        Assert.True(output.Truncated);
        // Another thread does not see it.
        Assert.Null(_processes.Output(Guid.NewGuid(), started.Id!, 10));

        var stopped = _processes.Stop(_thread, started.Id!, 10);
        Assert.NotNull(stopped);
        Assert.True(stopped!.Running);
        Assert.Contains("booting", stopped.Output);
        Assert.Equal(0, _processes.Running(_thread));
        Assert.Null(_processes.Output(_thread, started.Id!, 10));
        Assert.Null(_processes.Stop(_thread, started.Id!, 10));
    }

    [Fact]
    public async Task A_process_is_ready_once_its_local_url_answers()
    {
        var port = FreePort();
        var url = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
            }
        });

        var started = await _processes.StartAsync(_thread, "sleep 30", _workspace.Root, new BackgroundWait(url, null, 20));
        if (started.Shell == "cmd.exe") return;

        Assert.True(started.Ready);
        Assert.False(started.Exited);
        Assert.True(started.WaitedMs < 15_000);
    }

    [Fact]
    public async Task A_process_that_exits_reports_its_exit_code()
    {
        var started = await _processes.StartAsync(_thread, "echo done; exit 3", _workspace.Root, new BackgroundWait(null, "never shows", 10));
        if (started.Shell == "cmd.exe") return;

        Assert.False(started.Ready);
        Assert.True(started.Exited);
        Assert.Equal(3, started.ExitCode);
        Assert.Contains("done", started.Output);
        Assert.Equal(0, _processes.Running(_thread));
    }

    [Fact]
    public async Task A_thread_runs_at_most_three_and_all_stop_when_it_ends()
    {
        for (var i = 0; i < BackgroundProcesses.MaxPerThread; i++)
        {
            var started = await _processes.StartAsync(_thread, "sleep 30", _workspace.Root, new BackgroundWait(null, null, 1));
            if (started.Shell == "cmd.exe") return;
            Assert.NotNull(started.Id);
            Assert.True(started.Ready);
        }

        var fourth = await _processes.StartAsync(_thread, "sleep 30", _workspace.Root, new BackgroundWait(null, null, 1));
        Assert.Null(fourth.Id);
        Assert.Contains("stop one", fourth.Error);

        var other = Guid.NewGuid();
        Assert.NotNull((await _processes.StartAsync(other, "sleep 30", _workspace.Root, new BackgroundWait(null, null, 1))).Id);

        Assert.Equal(BackgroundProcesses.MaxPerThread, _processes.StopThread(_thread));
        Assert.Equal(0, _processes.Running(_thread));
        Assert.Equal(1, _processes.Running(other));
    }

    [Fact]
    public async Task The_planner_starts_a_process_after_permission_and_it_stops_when_the_thread_ends()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.StartProcess, new { command = "echo server-up; sleep 60", reason = "jalankan server", wait_for_text = "server-up", wait_seconds = 20 }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Server berjalan." }));

        var (first, firstObserver) = await harness.RunAsync("Jalankan server lalu cek", ThreadSource.ClaudeCode, "session-bg");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains("echo server-up; sleep 60", firstObserver.Final);
        Assert.Contains("Izinkan sekali", firstObserver.Final);
        var processes = harness.Service<IBackgroundProcesses>();
        Assert.Equal(0, processes.Running(first.ThreadId));

        var (second, secondObserver) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-bg");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Contains("Server berjalan.", secondObserver.Final);
        var toolMessage = router.CallsTo(ScriptedAgent.Planner)[1].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool");
        var content = toolMessage["content"]!.GetValue<string>();
        if (!content.Contains("cmd.exe"))
        {
            Assert.Contains("is running", content);
            Assert.Contains("server-up", content);
        }
        // The thread ended: its process is gone.
        Assert.Equal(0, processes.Running(second.ThreadId));
    }

    [Fact]
    public void The_tools_are_offered_with_run_command_only()
    {
        using var _ = Lang.Use("en");
        static IEnumerable<string> Names(JsonArray tools) => tools.Select(t => t!["function"]!["name"]!.GetValue<string>());

        Assert.Contains(PlannerTools.StartProcess, Names(PlannerTools.All(canCommand: true)));
        Assert.Contains(PlannerTools.StopProcess, Names(PlannerTools.All(canCommand: true)));
        Assert.DoesNotContain(PlannerTools.StartProcess, Names(PlannerTools.All(canCommand: false)));
        Assert.DoesNotContain(PlannerTools.StartProcess, PlannerTools.Delegable);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

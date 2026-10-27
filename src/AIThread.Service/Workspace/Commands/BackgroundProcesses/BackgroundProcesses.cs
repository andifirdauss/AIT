using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Workspace.Commands;

// How start_process waits before it answers: until Url (localhost) answers with any HTTP status, Text shows in the
// output, or Seconds pass, whichever comes first; it stops waiting early when the process exits.
public sealed record BackgroundWait(string? Url, string? Text, int Seconds);

// What start_process got. Id is null when nothing runs (Error says why). Ready: the awaited URL answered or text
// showed; without either, the process is still running after the wait. Output: what it printed so far.
public sealed record BackgroundStart(string? Id, string Shell, bool Ready, bool Exited, int? ExitCode, string Output, long WaitedMs, string? Error = null);

// One process as process_output and stop_process see it: the last lines of its output (Truncated when lines were
// left out) and whether it still runs. Expired: stopped by AIT after BackgroundProcesses.MaxAge.
public sealed record BackgroundOutput(string Id, string Command, string WorkingDirectory, bool Running, int? ExitCode, bool Expired, string Output, bool Truncated, TimeSpan Age);

// The planner's background processes (start_process): a dev server or watcher that keeps running while the planner
// investigates, e.g. `npm run dev` before a curl to it. Same shell and environment as run_command (CommandRunner).
// Each belongs to one thread, at most MaxPerThread run at once, none outlives MaxAge, and all of a thread's are stopped
// when the thread ends (ThreadOrchestrator) or AIT stops (Dispose).
public sealed partial class BackgroundProcesses(ILogger<BackgroundProcesses> logger) : IBackgroundProcesses, IDisposable
{
    public const int MaxPerThread = 3;
    public const int MaxWaitSeconds = 120;
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    // Kept per process; older output is dropped first.
    public const int BufferChars = 200_000;

    // How often start_process looks at the output, and at the awaited URL.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UrlInterval = TimeSpan.FromSeconds(1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly ConcurrentDictionary<string, Entry> _processes = new(StringComparer.OrdinalIgnoreCase);
    private int _counter;

    private sealed class Entry(Guid threadId, string id, string command, string workingDirectory, string shell, Process process) : IDisposable
    {
        private readonly object _gate = new();
        private readonly StringBuilder _output = new();

        public Guid ThreadId { get; } = threadId;
        public string Id { get; } = id;
        public string Command { get; } = command;
        public string WorkingDirectory { get; } = workingDirectory;
        public string Shell { get; } = shell;
        public Process Process { get; } = process;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public Timer? Lifetime { get; set; }
        public bool Expired { get; set; }
        public bool Dropped { get; private set; }

        public bool Running
        {
            get
            {
                try
                {
                    return !Process.HasExited;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    return false;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                try
                {
                    return Process.HasExited ? Process.ExitCode : null;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    return null;
                }
            }
        }

        public void Append(string? line)
        {
            if (line is null) return;
            lock (_gate)
            {
                _output.Append(line).Append('\n');
                if (_output.Length <= BufferChars) return;
                _output.Remove(0, _output.Length - BufferChars);
                Dropped = true;
            }
        }

        public string Text()
        {
            lock (_gate) return _output.ToString();
        }

        public void Dispose()
        {
            Lifetime?.Dispose();
            CommandRunner.Kill(Process);
            Process.Dispose();
        }
    }
}

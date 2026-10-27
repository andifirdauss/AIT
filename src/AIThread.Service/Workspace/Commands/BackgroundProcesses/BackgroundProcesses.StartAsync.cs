using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    public async Task<BackgroundStart> StartAsync(Guid threadId, string command, string workingDirectory, BackgroundWait wait, CancellationToken cancellationToken = default)
    {
        if (Running(threadId) >= MaxPerThread)
            return new BackgroundStart(null, string.Empty, false, false, null, string.Empty, 0,
                $"this thread already runs {MaxPerThread} background processes; stop one with stop_process first");

        var startInfo = CommandRunner.StartInfo(command, workingDirectory, out var shell);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var id = $"p{Interlocked.Increment(ref _counter)}";
        var entry = new Entry(threadId, id, command, workingDirectory, shell, process);
        process.OutputDataReceived += (_, e) => entry.Append(e.Data);
        process.ErrorDataReceived += (_, e) => entry.Append(e.Data);
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            return new BackgroundStart(null, shell, false, false, null, string.Empty, 0, $"{shell} could not start: {ex.Message}");
        }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        entry.Lifetime = new Timer(_ => Expire(entry), null, MaxAge, Timeout.InfiniteTimeSpan);
        _processes[id] = entry;
        logger.LogInformation("Background process {Id} started for thread {ThreadId} in {Directory}", id, threadId, workingDirectory);

        var clock = Stopwatch.StartNew();
        var ready = await WaitAsync(entry, wait, cancellationToken);
        return new BackgroundStart(id, shell, ready, !entry.Running, entry.ExitCode, entry.Text(), clock.ElapsedMilliseconds);
    }
}

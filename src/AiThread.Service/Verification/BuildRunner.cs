using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Verification;

public sealed record BuildResult(
    string Command,
    bool Succeeded,
    bool TimedOut,
    int? ExitCode,
    IReadOnlyList<BuildDiagnostic> Errors,
    int WarningCount,
    string OutputTail,
    long DurationMs,
    string? StartError = null)
{
    // Every error is an output copy blocked by a running program; the code compiled.
    public bool OnlyLockedOutput => !Succeeded && !TimedOut && StartError is null && Errors.Count > 0 && Errors.All(e => e.IsLockedOutput);

    // Good enough to stop fixing: the build succeeded or only failed to overwrite locked binaries.
    public bool Passed => Succeeded || OnlyLockedOutput;
}

// Runs a BuildCommand: one build per project root at a time (two builds would fight over obj/), stopped together
// with its child processes on timeout or cancellation. Diagnostics are parsed while the output streams, so a long
// log cannot push the errors out of memory.
public sealed class BuildRunner(ILogger<BuildRunner> logger)
{
    private const int TailLines = 120;

    // Build servers started by the build (MSBuild nodes, the compiler server) can inherit the output pipes and keep
    // them open after the build exits; whatever the build wrote is read by then.
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(command.Root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunCoreAsync(command, timeout, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BuildResult> RunCoreAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var parser = new BuildOutputParser(command.WorkingDirectory);
        var tail = new Queue<string>();
        void OnLine(string line)
        {
            lock (tail)
            {
                parser.Add(line);
                tail.Enqueue(line);
                if (tail.Count > TailLines) tail.Dequeue();
            }
        }

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        PrepareEnvironment(startInfo.Environment);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Build command {Command} could not start", command.Display);
            return new BuildResult(command.Display, false, false, null, [], 0, string.Empty, 0,
                $"'{command.FileName}' could not be started ({ex.Message}). Is the {command.Stack} toolchain installed and on PATH?");
        }

        // Nothing may wait for keyboard input.
        process.StandardInput.Close();
        var pumps = Task.WhenAll(PumpAsync(process.StandardOutput, OnLine), PumpAsync(process.StandardError, OnLine));

        var timedOut = false;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                await KillAsync(process);
                cancellationToken.ThrowIfCancellationRequested();
                timedOut = true;
            }
        }

        try
        {
            await pumps.WaitAsync(DrainGrace, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // See DrainGrace.
        }
        stopwatch.Stop();

        int? exitCode = process.HasExited ? process.ExitCode : null;
        string outputTail;
        lock (tail) outputTail = string.Join('\n', tail);

        return new BuildResult(command.Display, !timedOut && exitCode == 0, timedOut, exitCode,
            parser.Errors.ToList(), parser.WarningCount, outputTail, stopwatch.ElapsedMilliseconds);
    }

    private async Task KillAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            using var wait = new CancellationTokenSource(KillWait);
            await process.WaitForExitAsync(wait.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not stop build process {ProcessId}", SafeId(process));
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line) onLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process is gone and its pipe with it.
        }
    }

    // English messages without colours or banners. MSBuild variables inherited from whatever started AiThread (an IDE,
    // dotnet run) could point the build at another SDK, so they are dropped.
    private static void PrepareEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var key in environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            environment.Remove(key);

        environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        environment["VSLANG"] = "1033";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        environment["MSBUILDTERMINALLOGGER"] = "off";
        environment["NO_COLOR"] = "1";
        environment["FORCE_COLOR"] = "0";
        environment["CARGO_TERM_COLOR"] = "never";
        environment["npm_config_color"] = "false";
        environment["npm_config_update_notifier"] = "false";
        environment["npm_config_fund"] = "false";
    }

    private static int? SafeId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

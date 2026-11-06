using System.ComponentModel;
using System.Diagnostics;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    public async Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var launch = ResolveLaunch() ?? throw NotInstalled(null);
        await _slots.WaitAsync(cancellationToken);
        var work = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var systemFile = Path.Combine(work, "system-prompt.md");
            await File.WriteAllTextAsync(systemFile, prompt.System.Length == 0 ? "You are a helpful assistant." : prompt.System, Utf8, cancellationToken);

            var startInfo = new ProcessStartInfo(launch.FileName)
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8
            };
            foreach (var argument in launch.Prefix.Concat(BuildArguments(model, systemFile))) startInfo.ArgumentList.Add(argument);
            CleanEnvironment(startInfo.Environment);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                throw NotInstalled(ex);
            }

            var reader = new ClaudeStreamReader();
            using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            total.CancelAfter(limits.Timeout);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            try
            {
                try
                {
                    await process.StandardInput.WriteLineAsync(InputLine(prompt).AsMemory(), total.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The CLI exited before reading its input; its output says why.
                }

                var stderr = process.StandardError.ReadToEndAsync(total.Token);
                while (true)
                {
                    stall.CancelAfter(limits.Stall);
                    var line = await process.StandardOutput.ReadLineAsync(stall.Token);
                    if (line is null) break;
                    var delta = reader.Feed(line);
                    if (delta is not null && onDelta is not null) await onDelta(delta);
                }
                await process.WaitForExitAsync(total.Token);
                return Output(model, reader, process.ExitCode, await stderr);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var seconds = (int)(total.IsCancellationRequested ? limits.Timeout : limits.Stall).TotalSeconds;
                throw new NineRouterException(
                    Lang.T($"Claude Code CLI tidak menjawab dalam {seconds} detik.", $"Claude Code CLI did not answer within {seconds} seconds."), 408);
            }
            finally
            {
                Kill(process);
            }
        }
        finally
        {
            _slots.Release();
            DeleteQuietly(work);
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Verification;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class AppLauncher
{
    public async Task<AppStartResult> StartAsync(AppLaunchCommand command, string baseUrl, CancellationToken cancellationToken)
    {
        var tail = new Queue<string>();
        void OnLine(string line)
        {
            // dotnet run builds first; its warnings (NU1903, CS8618, ...) would push the startup error out of the tail.
            if (string.IsNullOrWhiteSpace(line) || BuildWarning().IsMatch(line)) return;
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > TailLines) tail.Dequeue();
            }
        }
        string Output()
        {
            string text;
            lock (tail) text = string.Join('\n', tail).Trim('\n', '\r');
            return text.Length <= MaxTailChars ? text : "…" + text[^MaxTailChars..];
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
        BuildRunner.PrepareEnvironment(startInfo.Environment);
        foreach (var (key, value) in command.Environment) startInfo.Environment[key] = value;

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            logger.LogWarning("Aplikasi tidak bisa dijalankan dengan {Command}: {Error}", command.Display, ex.Message);
            return new AppStartResult(null, Lang.T($"'{command.FileName}' tidak bisa dijalankan ({ex.Message.TrimEnd('.')}); pastikan toolchain {command.Stack} terpasang dan ada di PATH", $"'{command.FileName}' cannot be run ({ex.Message.TrimEnd('.')}); make sure the {command.Stack} toolchain is installed and on PATH"));
        }

        logger.LogInformation("Menjalankan aplikasi untuk testing: {Command} (PID {ProcessId})", command.Display, process.Id);
        var app = new RunningApp(process, command);
        // Nothing may wait for keyboard input. The output is read for as long as the app runs, or its pipe fills up and
        // the app stops.
        process.StandardInput.Close();
        _ = PumpAsync(process.StandardOutput, OnLine);
        _ = PumpAsync(process.StandardError, OnLine);

        using var http = CreateClient();
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await AnswersAsync(http, baseUrl, cancellationToken)) return new AppStartResult(app, null);

                if (process.HasExited)
                {
                    var exitCode = process.ExitCode;
                    await app.DisposeAsync();
                    // Another instance (started meanwhile by the developer or a parallel thread) holds the port.
                    if (await AnswersAsync(http, baseUrl, cancellationToken)) return new AppStartResult(null, null);
                    // Windows crash codes read better in hex: -1073741819 is 0xC0000005 (access violation).
                    var code = exitCode < 0 ? $"{exitCode} (0x{exitCode:X8})" : exitCode.ToString();
                    return Failed(command, Lang.T($"berhenti dengan exit code {code} sebelum menjawab", $"stopped with exit code {code} before answering"), Output());
                }

                if (clock.Elapsed > StartTimeout)
                {
                    await app.DisposeAsync();
                    return Failed(command, Lang.T($"belum menjawab setelah {StartTimeout.TotalMinutes:0} menit, jadi dihentikan lagi", $"did not answer after {StartTimeout.TotalMinutes:0} minutes, so it was stopped again"), Output());
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private AppStartResult Failed(AppLaunchCommand command, string what, string output)
    {
        logger.LogWarning("Aplikasi untuk testing {What}: {Command}\n{Output}", what, command.Display, output);
        return new AppStartResult(null, $"{command.Display} {what}", output.Length == 0 ? null : output);
    }
}

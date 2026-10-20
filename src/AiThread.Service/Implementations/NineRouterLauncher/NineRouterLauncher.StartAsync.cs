using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private async Task<RouterLaunchResultDto> StartAsync(string node, NineRouterCli cli, int port, bool installed, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var source = cli.Source switch
        {
            NineRouterSource.Global => "npm global",
            NineRouterSource.Bundled => "paket aithread",
            _ => "AiThread:NineRouter:CliPath"
        };
        activityLog.Info(Category, $"Menyalakan 9router di port {port} ({source}: {cli.Path}).");

        // cmd redirects to a file so 9router keeps a valid stdout after AiThread exits.
        var command = $"\"{node}\" --dns-result-order=ipv4first \"{cli.Path}\" --tray --no-browser --skip-update -p {port} > \"{LogPath}\" 2>&1";
        var process = Process.Start(Shell(command, Path.GetDirectoryName(cli.Path)!, node))
                      ?? throw new InvalidOperationException("cmd.exe did not start.");
        Interlocked.Exchange(ref _started, process)?.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(10, Settings.StartTimeoutSeconds));
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            var status = await router.CheckStatusAsync(cancellationToken);
            if (status.IsOnline)
            {
                eventBus.Publish(AppEvent.Create(AppEventTypes.RouterStatus, status));
                var message = installed ? "9router dipasang dan dijalankan." : "9router dijalankan.";
                activityLog.Success(Category, $"{message} Log: {LogPath}");
                return new RouterLaunchResultDto(true, true, installed, message);
            }

            if (process.HasExited)
            {
                Interlocked.CompareExchange(ref _started, null, process);
                return Fail($"9router berhenti saat dinyalakan (exit {process.ExitCode}). {LogTail()}".TrimEnd());
            }
        }

        return Fail($"9router belum menjawab setelah {Settings.StartTimeoutSeconds} detik; prosesnya dibiarkan berjalan. Lihat {LogPath}.");
    }
}

using System.Diagnostics;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private async Task<RouterLaunchResultDto> StartAsync(string node, NineRouterCli cli, int port, bool installed, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var source = cli.Source switch
        {
            NineRouterSource.Global => "npm global",
            NineRouterSource.Bundled => Lang.T("paket aithread", "aithread package"),
            _ => "AIThread:NineRouter:CliPath"
        };
        activityLog.Info(Category, Lang.T($"Menyalakan 9router di port {port} ({source}: {cli.Path}).", $"Starting 9router on port {port} ({source}: {cli.Path})."));

        // cmd redirects to a file so 9router keeps a valid stdout after AIThread exits.
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
                var message = installed ? Lang.T("9router dipasang dan dijalankan.", "9router installed and started.") : Lang.T("9router dijalankan.", "9router started.");
                activityLog.Success(Category, $"{message} Log: {LogPath}");
                return new RouterLaunchResultDto(true, true, installed, message);
            }

            if (process.HasExited)
            {
                Interlocked.CompareExchange(ref _started, null, process);
                return Fail(Lang.T($"9router berhenti saat dinyalakan (exit {process.ExitCode}). {LogTail()}", $"9router stopped while starting (exit {process.ExitCode}). {LogTail()}").TrimEnd());
            }
        }

        return Fail(Lang.T($"9router belum menjawab setelah {Settings.StartTimeoutSeconds} detik; prosesnya dibiarkan berjalan. Lihat {LogPath}.", $"9router has not answered after {Settings.StartTimeoutSeconds} seconds; the process is left running. See {LogPath}."));
    }
}

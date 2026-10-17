using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AiThread.Common.DTOs;
using AiThread.Common.Options;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Implementations;

// Runs 9router the way its own "Hide to Tray" does (node cli.js --tray), in a hidden console whose output goes to
// %LOCALAPPDATA%\AiThread\logs\9router.log. On start 9router kills whatever holds its port, so it is only started
// when that port is free.
public sealed class NineRouterLauncher(
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AiThreadOptions> options,
    ILogger<NineRouterLauncher> logger) : INineRouterLauncher, IDisposable
{
    private const string Category = "9router";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // Per process, not per host: the exe's menu rebuilds the host when the port changes.
    private static int _autoInstallTried;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _started;

    private NineRouterLaunchOptions Settings => options.Value.NineRouter;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread", "logs", "9router.log");

    public async Task<RouterLaunchResultDto> EnsureRunningAsync(bool manual, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var status = await router.CheckStatusAsync(cancellationToken);
            if (status.IsOnline) return new RouterLaunchResultDto(true, false, false, "9router sudah berjalan.");

            if (!NineRouterLocator.TryGetLocalPort(status.BaseUrl, out var port))
                return Fail($"URL 9router ({status.BaseUrl}) bukan alamat lokal; AIT hanya bisa menyalakan 9router di komputer ini.");

            if (await IsListeningAsync(port, cancellationToken))
                return Fail($"Port {port} sudah dipakai, tetapi tidak menjawab sebagai 9router. AIT tidak menyalakan 9router karena 9router akan mematikan proses di port itu.");

            var dirs = NineRouterLocator.SearchDirs();
            var node = NineRouterLocator.FindExecutable("node.exe", Settings.NodePath, dirs, File.Exists);
            if (node is null)
                return Fail("Node.js tidak ditemukan. Pasang Node.js 18 atau lebih baru (https://nodejs.org), lalu coba lagi.");

            var cli = FindCli(dirs);
            var installed = false;
            if (cli is null)
            {
                var mayInstall = manual || (Settings.AutoInstall && Interlocked.Exchange(ref _autoInstallTried, 1) == 0);
                if (!mayInstall)
                    return Fail("9router belum terpasang. Pasang dengan `npm install -g 9router`, klik Jalankan 9router di Dashboard, atau aktifkan AiThread:NineRouter:AutoInstall.");

                var error = await InstallAsync(node, dirs, cancellationToken);
                if (error is not null) return Fail(error);
                cli = FindCli(NineRouterLocator.SearchDirs());
                if (cli is null) return Fail("npm install -g 9router selesai, tetapi cli.js 9router tidak ditemukan di folder global npm.");
                installed = true;
            }

            return await StartAsync(node, cli, port, installed, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StopStarted()
    {
        var process = Interlocked.Exchange(ref _started, null);
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                activityLog.Info(Category, "9router yang dijalankan AIT dihentikan.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Stopping 9router failed.");
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        // Leaves 9router running unless StopOnExit (handled by the worker); only the handle is released here.
        Interlocked.Exchange(ref _started, null)?.Dispose();
        _gate.Dispose();
    }

    private NineRouterCli? FindCli(IReadOnlyList<string> dirs) =>
        NineRouterLocator.FindCli(Settings.CliPath, dirs, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory, File.Exists);

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

    // npm is a .cmd script, so it runs through cmd. Its output is kept for the error message.
    private async Task<string?> InstallAsync(string node, IReadOnlyList<string> dirs, CancellationToken cancellationToken)
    {
        var nodeDir = Path.GetDirectoryName(node)!;
        var npm = NineRouterLocator.FindExecutable("npm.cmd", null, dirs.Prepend(nodeDir), File.Exists);
        if (npm is null) return "npm tidak ditemukan; pasang ulang Node.js (npm ikut terpasang), lalu coba lagi.";

        activityLog.Info(Category, "9router tidak ditemukan; memasang dengan npm install -g 9router …");
        var startInfo = Shell($"\"{npm}\" install -g {NineRouterLocator.PackageName} --no-fund --no-audit", nodeDir, node);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd.exe did not start.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, Settings.InstallTimeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return $"npm install -g 9router tidak selesai dalam {Settings.InstallTimeoutSeconds} detik.";
        }

        string text;
        lock (output) text = output.ToString();
        logger.LogInformation("npm install -g 9router exited {ExitCode}: {Output}", process.ExitCode, text);
        return process.ExitCode == 0 ? null : $"npm install -g 9router gagal (exit {process.ExitCode}). {Tail(text)}".TrimEnd();
    }

    // Node's folder goes first on PATH so 9router and npm scripts find the same node, even when it was installed
    // after AiThread started.
    private static ProcessStartInfo Shell(string command, string workingDirectory, string node)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            Arguments = $"/d /s /c \"{command}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, NineRouterLocator.SearchDirs().Prepend(Path.GetDirectoryName(node)!));
        return startInfo;
    }

    private static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await socket.ConnectAsync(address, port, timeout.Token);
                return true;
            }
            catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)) { }
        }
        return false;
    }

    private static string LogTail()
    {
        try { return File.Exists(LogPath) ? Tail(File.ReadAllText(LogPath)) : ""; }
        catch (IOException) { return ""; }
    }

    private static string Tail(string text, int lines = 5) =>
        string.Join(" | ", text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).TakeLast(lines));

    private RouterLaunchResultDto Fail(string message)
    {
        activityLog.Warn(Category, message);
        return new RouterLaunchResultDto(false, false, false, message);
    }
}

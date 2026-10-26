using System.Runtime.Versioning;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting;

// Interactive menu of a double-clicked or typed AIThread.exe. The server runs inside this process (logs go to a file),
// or, when an instance is already running (e.g. in the tray), the menu controls that one instead.
[SupportedOSPlatform("windows")]
public sealed class ConsoleMenu(int port)
{
    private readonly ServerHost _server = new();
    private readonly CancellationTokenSource _quit = new();
    private int _port = port;
    private bool _owned;
    private string? _startError;

    private static TextWriter Out => LogFile.ConsoleOut;

    public async Task<int> RunAsync()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // leave through the menu so the server stops gracefully
            _quit.Cancel();
        };
        Console.Title = "AIT";

        await EnsureServerAsync();

        while (!_quit.IsCancellationRequested)
        {
            using var client = new ServerClient(_port);
            var info = await client.GetInfoAsync();
            var running = info is not null;

            // The server can stop on its own (POST /api/system/shutdown): nothing left to show for an owned one.
            if (_owned && !running && !_server.IsRunning)
            {
                Out.WriteLine();
                Out.WriteLine(Lang.T("Server berhenti.", "Server stopped."));
                return 0;
            }

            Draw(client, info);
            var key = await ReadKeyAsync();
            if (key is null) break;

            switch (key.Value)
            {
                case '1': Cli.OpenBrowser(client.BaseUrl); break;
                case '2' when _owned: if (await HideToTrayAsync()) return 0; break;
                case '2' when info is null: await RestartAsync(); break;
                case '2': await StopExternalAsync(client); break;
                case '3': await ShowLogsAsync(); break;
                case '4' when _owned: await RestartAsync(); break;
                case '5' when _owned || info is null: await ChangePortAsync(); break;
                case '6': await ToggleAutostartAsync(); break;
                case '0': return await QuitAsync();
            }
        }

        return await QuitAsync();
    }

    private async Task EnsureServerAsync()
    {
        using var client = new ServerClient(_port);
        if (await client.GetInfoAsync() is not null)
        {
            _owned = false;
            return;
        }

        LogFile.Redirect();
        Out.WriteLine(Lang.T($"Menjalankan AIT di {client.BaseUrl} ...", $"Starting AIT at {client.BaseUrl} ..."));
        try
        {
            await _server.StartAsync([], _port);
            _owned = true;
            _startError = null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _owned = false;
            _startError = ex.Message;
        }
    }

    private void Draw(ServerClient client, AIThread.Common.DTOs.SystemInfoDto? info)
    {
        Console.Clear();
        Out.WriteLine();
        Out.WriteLine($"  AIT {Cli.Version}");
        Out.WriteLine("  ------------------------------------------------------------");
        if (info is not null)
        {
            var where = _owned ? Lang.T("proses ini", "this process") : "instance lain / tray";
            Write("  Server    : ", ConsoleColor.Gray);
            Write(Lang.T($"Berjalan di {client.BaseUrl}", $"Running at {client.BaseUrl}"), ConsoleColor.Green);
            Out.WriteLine($"  (PID {info.ProcessId}, {where})");
        }
        else
        {
            Write("  Server    : ", ConsoleColor.Gray);
            Write(Lang.T("Tidak berjalan", "Not running"), ConsoleColor.Red);
            Out.WriteLine(_startError is null ? string.Empty : $"  ({_startError})");
        }

        Out.WriteLine(Lang.T($"  Log       : {LogFile.CurrentPath ?? AppPaths.LatestLog()?.FullName ?? "(belum ada)"}", $"  Log       : {LogFile.CurrentPath ?? AppPaths.LatestLog()?.FullName ?? "(none yet)"}"));
        Out.WriteLine(Lang.T($"  Autostart : {(Autostart.IsEnabled() ? "aktif (tray saat login Windows)" : "tidak aktif")}", $"  Autostart : {(Autostart.IsEnabled() ? "on (tray at Windows login)" : "off")}"));
        Out.WriteLine();
        Out.WriteLine(Lang.T("  [1] Web UI (buka di browser)", "  [1] Web UI (open in the browser)"));
        if (_owned)
        {
            Out.WriteLine(Lang.T("  [2] Sembunyikan ke Tray (background)", "  [2] Hide to the tray (background)"));
            Out.WriteLine("  [3] Log langsung");
            Out.WriteLine("  [4] Restart server");
            Out.WriteLine(Lang.T($"  [5] Ganti port (sekarang {_port})", $"  [5] Change port (now {_port})"));
        }
        else
        {
            Out.WriteLine(info is null ? Lang.T("  [2] Coba jalankan server lagi", "  [2] Try to start the server again") : Lang.T("  [2] Matikan instance yang berjalan, lalu jalankan di jendela ini", "  [2] Stop the running instance, then run in this window"));
            Out.WriteLine(Lang.T("  [3] Log terakhir", "  [3] Latest log"));
            if (info is null) Out.WriteLine(Lang.T($"  [5] Ganti port (sekarang {_port})", $"  [5] Change port (now {_port})"));
        }

        Out.WriteLine(Lang.T($"  [6] Autostart saat login Windows: {(Autostart.IsEnabled() ? "matikan" : "aktifkan")}", $"  [6] Autostart at Windows login: {(Autostart.IsEnabled() ? "turn off" : "turn on")}"));
        Out.WriteLine(_owned ? Lang.T("  [0] Keluar (matikan server)", "  [0] Exit (stop the server)") : Lang.T("  [0] Keluar (instance tetap berjalan)", "  [0] Exit (the instance keeps running)"));
        Out.WriteLine();
        Out.Write(Lang.T("  Pilih: ", "  Choose: "));
        Out.Flush();
    }

    private static void Write(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Out.Write(text);
        Console.ResetColor();
    }

    // Polls instead of blocking in ReadKey, so Ctrl+C and a server that stopped on its own are noticed.
    private async Task<char?> ReadKeyAsync()
    {
        try
        {
            while (!Console.KeyAvailable)
            {
                if (_owned && _server.Stopped.IsCompleted) return '\r';
                await Task.Delay(150, _quit.Token);
            }
        }
        catch (OperationCanceledException) { return null; }

        return Console.ReadKey(intercept: true).KeyChar;
    }

    private async Task<bool> HideToTrayAsync()
    {
        Out.WriteLine();
        Out.WriteLine(Lang.T("Memindahkan AIT ke tray ...", "Moving AIT to the tray ..."));
        if (!Autostart.CanEnable)
        {
            Out.WriteLine(Lang.T("Hanya tersedia untuk AIThread.exe hasil publish.", "Only available for a published AIThread.exe."));
            await PauseAsync();
            return false;
        }

        // The tray instance is a new process, so this server stops first (running threads are cancelled and recorded).
        await _server.StopAsync();
        Cli.SpawnTray(_port);

        using var client = new ServerClient(_port);
        var ready = await Cli.WaitUntilAsync(async () => await client.GetInfoAsync() is not null, TimeSpan.FromSeconds(90));
        if (ready)
        {
            Out.WriteLine(Lang.T($"AIT berjalan di tray: {client.BaseUrl}. Jendela ini boleh ditutup.", $"AIT is running in the tray: {client.BaseUrl}. This window can be closed."));
            await Task.Delay(1500);
            return true;
        }

        Out.WriteLine(Lang.T("AIT di tray belum menjawab. Periksa log di: ", "AIT in the tray is not answering yet. Check the log in: ") + AppPaths.LogDir);
        await PauseAsync();
        return true;
    }

    private async Task StopExternalAsync(ServerClient client)
    {
        Out.WriteLine();
        if (await client.GetInfoAsync() is null) return;
        Out.WriteLine(Lang.T("Mematikan instance yang berjalan ...", "Stopping the running instance ..."));
        if (!await client.ShutdownAsync())
        {
            Out.WriteLine(Lang.T("Permintaan shutdown ditolak.", "Shutdown request refused."));
            await PauseAsync();
            return;
        }

        await Cli.WaitUntilAsync(async () => await client.GetInfoAsync() is null, TimeSpan.FromSeconds(45));
        // Offer to take over: the menu owns the server from here on.
        await EnsureServerAsync();
    }

    private async Task ShowLogsAsync()
    {
        var path = _owned ? LogFile.CurrentPath : AppPaths.LatestLog()?.FullName;
        if (path is null)
        {
            Out.WriteLine();
            Out.WriteLine(Lang.T("Belum ada log.", "No log yet."));
            await PauseAsync();
            return;
        }

        Console.Clear();
        Out.WriteLine($"  {path}");
        Out.WriteLine(Lang.T("  Tekan tombol apa pun untuk kembali ke menu.", "  Press any key to go back to the menu."));
        Out.WriteLine("  ------------------------------------------------------------");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
        var tail = LogFile.TailAsync(path, 30, Out, stop.Token);
        try
        {
            while (!Console.KeyAvailable && !_quit.IsCancellationRequested) await Task.Delay(100);
            if (Console.KeyAvailable) Console.ReadKey(intercept: true);
        }
        finally
        {
            await stop.CancelAsync();
            await tail;
        }
    }

    private async Task RestartAsync()
    {
        Out.WriteLine();
        Out.WriteLine("Merestart server ...");
        await _server.StopAsync();
        await EnsureServerAsync();
    }

    private async Task ChangePortAsync()
    {
        Out.WriteLine();
        Out.Write(Lang.T($"  Port baru (1-65535, kosong = batal, sekarang {_port}): ", $"  New port (1-65535, empty = cancel, now {_port}): "));
        Out.Flush();
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (!int.TryParse(input, out var newPort) || newPort is < 1 or > 65535)
        {
            Out.WriteLine(Lang.T("Port tidak valid.", "Invalid port."));
            await PauseAsync();
            return;
        }

        if (newPort == _port) return;

        var oldPort = _port;
        await _server.StopAsync();
        _port = newPort;
        await EnsureServerAsync();
        if (!_owned)
        {
            // Port taken: go back to the working one rather than leave the user without a server.
            Out.WriteLine(Lang.T($"Port {newPort} tidak bisa dipakai. Kembali ke port {oldPort}.", $"Port {newPort} cannot be used. Back to port {oldPort}."));
            _port = oldPort;
            await EnsureServerAsync();
            await PauseAsync();
            return;
        }

        new CliSettings { Port = _port }.Save();
        Out.WriteLine(Lang.T($"Port disimpan: {_port}. Terapkan ulang integrasi CLI (menu CLI) karena alamatnya berubah.", $"Port saved: {_port}. Apply the CLI integrations again (CLI menu) because the address changed."));
        await PauseAsync();
    }

    private static async Task ToggleAutostartAsync()
    {
        Out.WriteLine();
        try
        {
            Autostart.Toggle();
        }
        catch (Exception ex)
        {
            Out.WriteLine(ex.Message);
            await PauseAsync();
        }
    }

    private async Task<int> QuitAsync()
    {
        if (_owned)
        {
            Out.WriteLine();
            Out.WriteLine(Lang.T("Mematikan AIT (thread yang berjalan dibatalkan dan dicatat) ...", "Stopping AIT (running threads are cancelled and recorded) ..."));
            await _server.StopAsync();
        }

        return 0;
    }

    private static async Task PauseAsync()
    {
        Out.WriteLine(Lang.T("Tekan tombol apa pun untuk lanjut ...", "Press any key to continue ..."));
        while (!Console.KeyAvailable) await Task.Delay(100);
        Console.ReadKey(intercept: true);
    }
}

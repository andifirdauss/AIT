using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AIThread.Common.Localization;
using AIThread.Data;

namespace AIThread.UI.Hosting;

// Entry point of AIThread.exe. Command-line arguments pick what runs; with none, a double-clicked or typed exe shows
// the interactive menu. Anything unrecognised (--urls, --environment, ...) runs the plain web host, so dotnet run,
// Visual Studio and scripts/aithread.ps1 behave as before.
[SupportedOSPlatform("windows")]
public static class Cli
{
    private const string HelpIndonesian = """
        AIThread - proxy, planner, dan orkestrasi multi-agent untuk CLI coding (Claude Code, Codex, Gemini CLI, ...)

        Pemakaian: AIThread.exe [perintah] [opsi]

          (tanpa perintah)  Menu interaktif: Web UI, sembunyikan ke tray, log, restart, port, autostart
          serve             Jalankan server di jendela ini, log tampil di konsol (Ctrl+C untuk berhenti)
          tray              Jalankan di background dengan ikon tray, tanpa jendela
          stop              Matikan instance yang berjalan dengan rapi
          status            Tampilkan status instance, port, log, dan autostart
          logs              Tampilkan log terbaru (-f untuk terus mengikuti)
          open              Buka Web UI di browser
          autostart on|off  Jalankan tray otomatis saat login Windows
          setup             Buka halaman Setup (semua langkah awal di satu halaman)
          setup export <f>  Tulis profil setup tim ke file f (tanpa API key dan password)
          setup import <f>  Terapkan profil setup tim dari file f; API key dan password ditanyakan di sini
          version           Tampilkan versi
          help              Bantuan ini

        Opsi:
          --port <n>        Port server (default 5225 atau yang terakhir disimpan dari menu)
          --open            serve/tray: buka Web UI di browser setelah siap
          -f                logs: terus ikuti log baru
          --yes             setup import: terapkan tanpa konfirmasi

        Konfigurasi ada di appsettings.json di samping AIThread.exe; data dan log di %LOCALAPPDATA%\AIThread.
        """;

    private const string HelpEnglish = """
        AIThread - proxy, planner and multi-agent orchestration for coding CLIs (Claude Code, Codex, Gemini CLI, ...)

        Usage: AIThread.exe [command] [options]

          (no command)      Interactive menu: Web UI, hide to the tray, log, restart, port, autostart
          serve             Run the server in this window, log shown in the console (Ctrl+C to stop)
          tray              Run in the background with a tray icon, without a window
          stop              Stop the running instance cleanly
          status            Show the status of the instance, port, log and autostart
          logs              Show the latest log (-f to keep following)
          open              Open the Web UI in the browser
          autostart on|off  Run the tray automatically at Windows login
          setup             Open the Setup page (every first step on one page)
          setup export <f>  Write the team setup profile to file f (without API keys and passwords)
          setup import <f>  Apply the team setup profile from file f; API keys and passwords are asked here
          version           Show the version
          help              This help

        Options:
          --port <n>        Server port (default 5225 or the one last saved from the menu)
          --open            serve/tray: open the Web UI in the browser once ready
          -f                logs: keep following new log lines
          --yes             setup import: apply without confirmation

        Configuration is in appsettings.json next to AIThread.exe; data and logs in %LOCALAPPDATA%\AIThread.
        """;

    private static string Help => Lang.T(HelpIndonesian, HelpEnglish);

    private static readonly string[] Commands = ["serve", "tray", "stop", "status", "logs", "open", "autostart", "setup", "version", "help", "menu"];

    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    public static async Task<int> RunAsync(string[] args)
    {
        // The menu and the commands speak the language chosen in the web UI; the server applies its own on start.
        Lang.Set(CliSettings.Load().Language);
        var command = args.Length > 0 && Array.IndexOf(Commands, args[0].ToLowerInvariant()) >= 0 ? args[0].ToLowerInvariant() : null;
        var optionArgs = args;

        if (command is not null) optionArgs = args[1..];
        else if (args.Length == 0 && MenuAllowed()) command = "menu";
        else return await RunPlainAsync(args);

        var options = Options.Parse(optionArgs);
        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            return 2;
        }

        var port = options.Port ?? CliSettings.Load().Port;
        switch (command)
        {
            case "menu": return await new ConsoleMenu(port).RunAsync();
            case "serve": return await ServeAsync(port, options.Open);
            case "tray": return await TrayAsync(port, options);
            case "stop": return await StopAsync(port);
            case "status": return await StatusAsync(port);
            case "logs": return await LogsAsync(options.Follow);
            case "open": OpenBrowser($"http://localhost:{port}"); return 0;
            case "autostart": return AutostartCommand(options.Rest);
            case "setup": return await SetupCommand.RunAsync(port, options.Rest, options.Yes);
            case "version": Console.WriteLine($"AIT {Version}"); return 0;
            default: Console.WriteLine(Help); return 0;
        }
    }

    // The original Program.cs flow; its configuration comes from args, environment variables and launch profiles.
    private static async Task<int> RunPlainAsync(string[] args)
    {
        var app = ServerHost.Build(args, port: null);
        await app.Services.InitializeDataLayerAsync();
        app.Run();
        return 0;
    }

    // Menu only for a person at a terminal: not under a debugger, not when a launcher chose the environment or URLs.
    private static bool MenuAllowed() =>
        !Debugger.IsAttached
        && !Console.IsInputRedirected
        && !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null
        && Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") is null
        && Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") is null;

    private static async Task<int> ServeAsync(int port, bool open)
    {
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is { } running)
        {
            Console.Error.WriteLine(Lang.T($"AIT sudah berjalan di {client.BaseUrl} (PID {running.ProcessId}). Jalankan 'AIThread.exe stop' dulu.", $"AIT is already running at {client.BaseUrl} (PID {running.ProcessId}). Run 'AIThread.exe stop' first."));
            return 1;
        }

        var app = ServerHost.Build([], port);
        await app.Services.InitializeDataLayerAsync();
        if (open) app.Lifetime.ApplicationStarted.Register(() => OpenBrowser(client.BaseUrl));
        await app.RunAsync();
        return 0;
    }

    private static async Task<int> TrayAsync(int port, Options options)
    {
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is not null)
        {
            // Typically a second double-click or a duplicate autostart entry: just show the running instance.
            OpenBrowser(client.BaseUrl);
            return 0;
        }

        LogFile.Redirect();
        // Autostart starts this exe as a console app, so Windows opens an empty console window. Output already goes to the
        // log file, so the console is released here and the window closes.
        FreeConsole();
        var server = new ServerHost();
        try
        {
            await server.StartAsync([], port);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            ShowError(Lang.T($"AIT gagal start di port {port}.\n\n{ex.Message}\n\nLog: {LogFile.CurrentPath}", $"AIT failed to start on port {port}.\n\n{ex.Message}\n\nLog: {LogFile.CurrentPath}"));
            await server.StopAsync();
            return 1;
        }

        var stopping = false;
        TrayIcon? tray = null;
        void Exit()
        {
            if (stopping) return;
            stopping = true;
            tray!.SetTooltip(Lang.T("AIT - menghentikan ...", "AIT - stopping ..."));
            _ = Task.Run(async () =>
            {
                await server.StopAsync();
                tray.Close();
            });
        }

        tray = new TrayIcon(
            $"AIT - http://localhost:{port}",
            Lang.T($"Berjalan di http://localhost:{port}. Klik kanan ikon ini untuk menu.", $"Running at http://localhost:{port}. Right-click this icon for the menu."),
            [
                new TrayIcon.Item(Lang.T("Buka Web UI", "Open Web UI"), () => OpenBrowser(client.BaseUrl)),
                new TrayIcon.Item(Lang.T("Lihat log", "View log"), () => OpenPath(LogFile.CurrentPath!)),
                new TrayIcon.Item(Lang.T("Buka folder log", "Open the log folder"), () => OpenPath(AppPaths.LogDir)),
                TrayIcon.Item.Separator,
                new TrayIcon.Item(Lang.T("Jalankan saat login Windows", "Run at Windows login"), () =>
                {
                    try { Autostart.Toggle(); }
                    catch (Exception ex) { ShowError(ex.Message); }
                }, Autostart.IsEnabled),
                TrayIcon.Item.Separator,
                new TrayIcon.Item(Lang.T("Keluar (matikan AIT)", "Exit (stop AIT)"), Exit)
            ],
            onDefault: () => OpenBrowser(client.BaseUrl));

        // A stop that did not come from the tray, e.g. POST /api/system/shutdown from 'AIThread.exe stop'.
        _ = server.Stopped.ContinueWith(_ => tray.Close());

        if (options.Open) OpenBrowser(client.BaseUrl);
        tray.Run();
        await server.StopAsync();
        return 0;
    }

    private static async Task<int> StopAsync(int port)
    {
        using var client = new ServerClient(port);
        var info = await client.GetInfoAsync();
        if (info is null)
        {
            Console.WriteLine(Lang.T($"AIT tidak berjalan di {client.BaseUrl}.", $"AIT is not running at {client.BaseUrl}."));
            return 0;
        }

        Console.WriteLine(Lang.T($"Mematikan AIT (PID {info.ProcessId}) ...", $"Stopping AIT (PID {info.ProcessId}) ..."));
        if (!await client.ShutdownAsync())
        {
            Console.Error.WriteLine(Lang.T("Permintaan shutdown ditolak. Tutup prosesnya secara manual.", "Shutdown request refused. Close the process by hand."));
            return 1;
        }

        if (!await WaitUntilAsync(async () => await client.GetInfoAsync() is null, TimeSpan.FromSeconds(45)))
        {
            Console.Error.WriteLine(Lang.T("AIT belum berhenti setelah 45 detik.", "AIT did not stop after 45 seconds."));
            return 1;
        }

        Console.WriteLine(Lang.T("AIT berhenti.", "AIT stopped."));
        return 0;
    }

    private static async Task<int> StatusAsync(int port)
    {
        using var client = new ServerClient(port);
        var info = await client.GetInfoAsync();
        if (info is null) Console.WriteLine(Lang.T($"AIT tidak berjalan di {client.BaseUrl}.", $"AIT is not running at {client.BaseUrl}."));
        else
        {
            Console.WriteLine(Lang.T($"AIT berjalan di {client.BaseUrl}", $"AIT running at {client.BaseUrl}"));
            Console.WriteLine($"  PID       : {info.ProcessId}");
            Console.WriteLine(Lang.T($"  Sejak     : {info.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}", $"  Since     : {info.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
            Console.WriteLine($"  Folder    : {info.ContentRoot}");
        }

        Console.WriteLine(Lang.T($"  Log       : {AppPaths.LatestLog()?.FullName ?? "(belum ada)"}", $"  Log       : {AppPaths.LatestLog()?.FullName ?? "(none yet)"}"));
        Console.WriteLine(Lang.T($"  Autostart : {(Autostart.IsEnabled() ? "aktif (saat login Windows)" : "tidak aktif")}", $"  Autostart : {(Autostart.IsEnabled() ? "on (at Windows login)" : "off")}"));
        return 0;
    }

    private static async Task<int> LogsAsync(bool follow)
    {
        var log = AppPaths.LatestLog();
        if (log is null)
        {
            Console.WriteLine(Lang.T($"Belum ada log di {AppPaths.LogDir}.", $"No log yet in {AppPaths.LogDir}."));
            return 0;
        }

        Console.WriteLine($"---- {log.FullName}");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        if (follow) await LogFile.TailAsync(log.FullName, 80, Console.Out, stop.Token);
        else
        {
            // Shared read: the running instance keeps the file open for writing.
            using var reader = new StreamReader(new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var recent = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                recent.Enqueue(line);
                if (recent.Count > 80) recent.Dequeue();
            }
            foreach (var line in recent) Console.WriteLine(line);
        }
        return 0;
    }

    private static int AutostartCommand(IReadOnlyList<string> rest)
    {
        var value = rest.FirstOrDefault()?.ToLowerInvariant();
        try
        {
            switch (value)
            {
                case "on": Autostart.Enable(); Console.WriteLine(Lang.T("Autostart aktif: AIT berjalan di tray saat login Windows.", "Autostart on: AIT runs in the tray at Windows login.")); return 0;
                case "off": Autostart.Disable(); Console.WriteLine(Lang.T("Autostart dimatikan.", "Autostart turned off.")); return 0;
                default: Console.Error.WriteLine(Lang.T("Pemakaian: AIThread.exe autostart on|off", "Usage: AIThread.exe autostart on|off")); return 2;
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static void OpenBrowser(string url) => OpenPath(url);

    // The shell opens URLs, folders and .log files with their default program.
    public static void OpenPath(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine(Lang.T($"Tidak bisa membuka {target}: {ex.Message}", $"Cannot open {target}: {ex.Message}")); }
    }

    // A detached copy in tray mode: no console window, survives the terminal that started it.
    public static void SpawnTray(int port)
    {
        var start = new ProcessStartInfo(AppPaths.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add("tray");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(port.ToString());
        Process.Start(start)?.Dispose();
    }

    public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(300);
        }
        return false;
    }

    // Tray mode has no console, so startup failures are shown in a dialog.
    private static void ShowError(string message) => MessageBox(0, message, "AIT", 0x10 /* MB_ICONERROR */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string caption, uint type);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private sealed record Options(int? Port, bool Open, bool Follow, IReadOnlyList<string> Rest, string? Error, bool Yes = false)
    {
        public static Options Parse(string[] args)
        {
            int? port = null;
            bool open = false, follow = false, yes = false;
            var rest = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var p) || p is < 1 or > 65535)
                            return new Options(null, false, false, rest, Lang.T("--port butuh angka 1-65535.", "--port needs a number 1-65535."));
                        port = p;
                        break;
                    case "--open": open = true; break;
                    case "-f" or "--follow": follow = true; break;
                    case "-y" or "--yes": yes = true; break;
                    default: rest.Add(args[i]); break;
                }
            }

            return new Options(port, open, follow, rest, null, yes);
        }
    }
}

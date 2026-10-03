using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AiThread.Data;

namespace AiThread.UI.Hosting;

// Entry point of AiThread.exe. Command-line arguments pick what runs; with none, a double-clicked or typed exe shows
// the interactive menu. Anything unrecognised (--urls, --environment, ...) runs the plain web host, so dotnet run,
// Visual Studio and scripts/aithread.ps1 behave as before.
[SupportedOSPlatform("windows")]
public static class Cli
{
    private const string Help = """
        AiThread - proxy, planner, dan orkestrasi multi-agent untuk Claude Code

        Pemakaian: AiThread.exe [perintah] [opsi]

          (tanpa perintah)  Menu interaktif: Web UI, sembunyikan ke tray, log, restart, port, autostart
          serve             Jalankan server di jendela ini, log tampil di konsol (Ctrl+C untuk berhenti)
          tray              Jalankan di background dengan ikon tray, tanpa jendela
          stop              Matikan instance yang berjalan dengan rapi
          status            Tampilkan status instance, port, log, dan autostart
          logs              Tampilkan log terbaru (-f untuk terus mengikuti)
          open              Buka Web UI di browser
          autostart on|off  Jalankan tray otomatis saat login Windows
          version           Tampilkan versi
          help              Bantuan ini

        Opsi:
          --port <n>        Port server (default 5225 atau yang terakhir disimpan dari menu)
          --open            serve/tray: buka Web UI di browser setelah siap
          -f                logs: terus ikuti log baru

        Konfigurasi ada di appsettings.json di samping AiThread.exe; data dan log di %LOCALAPPDATA%\AiThread.
        """;

    private static readonly string[] Commands = ["serve", "tray", "stop", "status", "logs", "open", "autostart", "version", "help", "menu"];

    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    public static async Task<int> RunAsync(string[] args)
    {
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
            case "version": Console.WriteLine($"AiThread {Version}"); return 0;
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
            Console.Error.WriteLine($"AiThread sudah berjalan di {client.BaseUrl} (PID {running.ProcessId}). Jalankan 'AiThread.exe stop' dulu.");
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
            ShowError($"AiThread gagal start di port {port}.\n\n{ex.Message}\n\nLog: {LogFile.CurrentPath}");
            await server.StopAsync();
            return 1;
        }

        var stopping = false;
        TrayIcon? tray = null;
        void Exit()
        {
            if (stopping) return;
            stopping = true;
            tray!.SetTooltip("AiThread - menghentikan ...");
            _ = Task.Run(async () =>
            {
                await server.StopAsync();
                tray.Close();
            });
        }

        tray = new TrayIcon(
            $"AiThread - http://localhost:{port}",
            $"Berjalan di http://localhost:{port}. Klik kanan ikon ini untuk menu.",
            [
                new TrayIcon.Item("Buka Web UI", () => OpenBrowser(client.BaseUrl)),
                new TrayIcon.Item("Lihat log", () => OpenPath(LogFile.CurrentPath!)),
                new TrayIcon.Item("Buka folder log", () => OpenPath(AppPaths.LogDir)),
                TrayIcon.Item.Separator,
                new TrayIcon.Item("Jalankan saat login Windows", () =>
                {
                    try { Autostart.Toggle(); }
                    catch (Exception ex) { ShowError(ex.Message); }
                }, Autostart.IsEnabled),
                TrayIcon.Item.Separator,
                new TrayIcon.Item("Keluar (matikan AiThread)", Exit)
            ],
            onDefault: () => OpenBrowser(client.BaseUrl));

        // A stop that did not come from the tray, e.g. POST /api/system/shutdown from 'AiThread.exe stop'.
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
            Console.WriteLine($"AiThread tidak berjalan di {client.BaseUrl}.");
            return 0;
        }

        Console.WriteLine($"Mematikan AiThread (PID {info.ProcessId}) ...");
        if (!await client.ShutdownAsync())
        {
            Console.Error.WriteLine("Permintaan shutdown ditolak. Tutup prosesnya secara manual.");
            return 1;
        }

        if (!await WaitUntilAsync(async () => await client.GetInfoAsync() is null, TimeSpan.FromSeconds(45)))
        {
            Console.Error.WriteLine("AiThread belum berhenti setelah 45 detik.");
            return 1;
        }

        Console.WriteLine("AiThread berhenti.");
        return 0;
    }

    private static async Task<int> StatusAsync(int port)
    {
        using var client = new ServerClient(port);
        var info = await client.GetInfoAsync();
        if (info is null) Console.WriteLine($"AiThread tidak berjalan di {client.BaseUrl}.");
        else
        {
            Console.WriteLine($"AiThread berjalan di {client.BaseUrl}");
            Console.WriteLine($"  PID       : {info.ProcessId}");
            Console.WriteLine($"  Sejak     : {info.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Folder    : {info.ContentRoot}");
        }

        Console.WriteLine($"  Log       : {AppPaths.LatestLog()?.FullName ?? "(belum ada)"}");
        Console.WriteLine($"  Autostart : {(Autostart.IsEnabled() ? "aktif (saat login Windows)" : "tidak aktif")}");
        return 0;
    }

    private static async Task<int> LogsAsync(bool follow)
    {
        var log = AppPaths.LatestLog();
        if (log is null)
        {
            Console.WriteLine($"Belum ada log di {AppPaths.LogDir}.");
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
                case "on": Autostart.Enable(); Console.WriteLine("Autostart aktif: AiThread berjalan di tray saat login Windows."); return 0;
                case "off": Autostart.Disable(); Console.WriteLine("Autostart dimatikan."); return 0;
                default: Console.Error.WriteLine("Pemakaian: AiThread.exe autostart on|off"); return 2;
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine($"Tidak bisa membuka {target}: {ex.Message}"); }
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
    private static void ShowError(string message) => MessageBox(0, message, "AiThread", 0x10 /* MB_ICONERROR */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string caption, uint type);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private sealed record Options(int? Port, bool Open, bool Follow, IReadOnlyList<string> Rest, string? Error)
    {
        public static Options Parse(string[] args)
        {
            int? port = null;
            bool open = false, follow = false;
            var rest = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var p) || p is < 1 or > 65535)
                            return new Options(null, false, false, rest, "--port butuh angka 1-65535.");
                        port = p;
                        break;
                    case "--open": open = true; break;
                    case "-f" or "--follow": follow = true; break;
                    default: rest.Add(args[i]); break;
                }
            }

            return new Options(port, open, follow, rest, null);
        }
    }
}

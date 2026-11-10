using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AIThread.Common.Localization;
using AIThread.Data;
using AIThread.Data.Messaging;
using AIThread.UI.Hosting.Agent;
using AIThread.UI.Hosting.Chat;
using Microsoft.Extensions.DependencyInjection;

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
          logs export [f]   Tulis ZIP diagnostik (insiden + stack trace, log aktivitas, file log) ke f; --days n (bawaan 3)
          open              Buka Web UI di browser
          autostart on|off  Jalankan tray otomatis saat login Windows
          chat              Chat terminal dengan AIT di project folder ini: prompt → thread, progress, izin, jawaban
          setup             Buka halaman Setup (semua langkah awal di satu halaman)
          setup export <f>  Tulis profil setup tim ke file f (tanpa API key dan password)
          setup import <f>  Terapkan profil setup tim dari file f; API key dan password ditanyakan di sini
          setup auto        Buat node dari CLI resmi yang terpasang di komputer ini (dilewati bila node sudah lengkap)
          doctor            Periksa prasyarat, server, setup, project folder ini, dan langkah berikutnya
          project add [p]   Daftarkan folder p sebagai project (bawaan: folder saat ini)
          thread wait <id>  Tunggu thread (exit 0 selesai, 1 gagal/batal, 3 menunggu izin/jawaban, 4 masih berjalan)
          thread approve <id> once|always|deny
                            Jawab permintaan izin thread
          skill on|off [p]  Nyalakan/matikan paksaan plugin AIT untuk folder p (bawaan: project folder ini)
          skill install|uninstall <cli>
                            Pasang/lepas skill + hook AIT di codex, gemini, opencode, qwen, omp, atau all
          version           Tampilkan versi
          help              Bantuan ini

        Opsi:
          --port <n>        Port server (default 5225 atau yang terakhir disimpan dari menu)
          --open            serve/tray: buka Web UI di browser setelah siap
          -f                logs: terus ikuti log baru
          --yes, -y         setup import: terapkan tanpa konfirmasi; chat -p: izinkan permintaan izin sekali
          --project <x>     chat: nama project terdaftar atau folder di dalamnya (bawaan: folder saat ini)
          -p, --prompt <t>  chat: jalankan satu prompt tanpa interaksi (exit 0 selesai, 1 gagal/batal,
                            2 argumen/project salah, 3 menunggu izin)
          -c, --continue    chat: lanjutkan sesi chat terakhir project ini
          -r, --resume [id] chat: lanjutkan sesi chat itu (tanpa id: pilih dari daftar)
          --detach          chat -p: mulai thread lalu langsung keluar; ikuti dengan 'thread wait' (-p - baca prompt dari stdin)
          --json            chat --detach, doctor, setup auto, project add, thread: satu dokumen JSON di stdout
          --start           doctor: jalankan server bila belum berjalan
          --timeout <s>     thread wait: batas tunggu dalam detik (bawaan 240)

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
          logs export [f]   Write the diagnostic ZIP (incidents + stack traces, activity log, log files) to f; --days n (default 3)
          open              Open the Web UI in the browser
          autostart on|off  Run the tray automatically at Windows login
          chat              Terminal chat with AIT in the project of this folder: prompt → thread, progress, permissions, answer
          setup             Open the Setup page (every first step on one page)
          setup export <f>  Write the team setup profile to file f (without API keys and passwords)
          setup import <f>  Apply the team setup profile from file f; API keys and passwords are asked here
          setup auto        Create the nodes from the official CLIs installed on this computer (skipped when the nodes are complete)
          doctor            Check the prerequisites, server, setup, this folder's project and the next step
          project add [p]   Register folder p as a project (default: the current folder)
          thread wait <id>  Wait for a thread (exit 0 completed, 1 failed/cancelled, 3 waiting for permission/an answer, 4 still running)
          thread approve <id> once|always|deny
                            Answer a thread's permission request
          skill on|off [p]  Turn the AIT plugin's enforcement on/off for folder p (default: this folder's project)
          skill install|uninstall <cli>
                            Add/remove AIT's skill + hook in codex, gemini, opencode, qwen, omp, or all
          version           Show the version
          help              This help

        Options:
          --port <n>        Server port (default 5225 or the one last saved from the menu)
          --open            serve/tray: open the Web UI in the browser once ready
          -f                logs: keep following new log lines
          --yes, -y         setup import: apply without confirmation; chat -p: allow permission requests once
          --project <x>     chat: name of a registered project or a folder inside it (default: the current folder)
          -p, --prompt <t>  chat: run one prompt without interaction (exit 0 completed, 1 failed/cancelled,
                            2 wrong arguments/project, 3 waiting for permission)
          -c, --continue    chat: resume this project's most recent chat session
          -r, --resume [id] chat: resume that chat session (no id: pick from the list)
          --detach          chat -p: start the thread and exit at once; follow it with 'thread wait' (-p - reads the prompt from stdin)
          --json            chat --detach, doctor, setup auto, project add, thread: one JSON document on stdout
          --start           doctor: start the server when it is not running
          --timeout <s>     thread wait: how long to wait, in seconds (default 240)

        Configuration is in appsettings.json next to AIThread.exe; data and logs in %LOCALAPPDATA%\AIThread.
        """;

    private static string Help => Lang.T(HelpIndonesian, HelpEnglish);

    private static readonly string[] Commands = ["serve", "tray", "stop", "status", "logs", "open", "autostart", "setup", "chat", "doctor", "project", "thread", "skill", "version", "help", "menu"];

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
            case "logs" when options.Rest.FirstOrDefault()?.ToLowerInvariant() == "export": return await LogsCommand.ExportAsync(port, options.Rest);
            case "logs": return await LogsAsync(options.Follow);
            case "open": OpenBrowser($"http://localhost:{port}"); return 0;
            case "autostart": return AutostartCommand(options.Rest);
            case "setup" when options.Rest.FirstOrDefault()?.ToLowerInvariant() == "auto": return await SetupAutoCommand.RunAsync(port, options.Json);
            case "setup": return await SetupCommand.RunAsync(port, options.Rest, options.Yes);
            case "doctor": return await DoctorCommand.RunAsync(port, options.Start, options.Json);
            case "project": return await ProjectCommand.RunAsync(port, options.Rest, options.Json);
            case "thread": return await ThreadCommand.RunAsync(port, options.Rest, options.Timeout, options.Json);
            case "skill": return await SkillCommand.RunAsync(port, options.Rest);
            case "chat" when options.Rest.Count > 0:
                Console.Error.WriteLine(Lang.T($"Argumen tidak dikenal: {string.Join(" ", options.Rest)}. Prompt diberikan lewat -p \"...\".", $"Unknown argument: {string.Join(" ", options.Rest)}. A prompt is given with -p \"...\"."));
                return 2;
            case "chat": return await ChatCommand.RunAsync(port, new ChatOptions(options.Project, options.Prompt, options.Yes, options.Continue, options.Resume, options.Detach, options.Json));
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
        // A plain start of this console app opens an empty console window. Output already goes to the log file, so the
        // console is released here and the window closes.
        FreeConsole();
        Autostart.Upgrade();
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
        using var notices = new CancellationTokenSource();
        if (server.Services?.GetService<IEventBus>() is { } events)
            _ = TrayNotifications.ForwardAsync(events, text => tray.Notify(text), notices.Token);
        tray.Run();
        await notices.CancelAsync();
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

    // A detached copy in tray mode: no console window, survives the terminal that started it. Started through the shell
    // so it inherits none of this process's handles: a redirected stdout (aithread chat -p | tee) still ends when this
    // process does.
    public static void SpawnTray(int port)
    {
        var start = new ProcessStartInfo(AppPaths.ExePath, $"tray --port {port}")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        Process.Start(start)?.Dispose();
    }

    // The running instance, or a new one in the tray (as 'AIThread.exe tray'), waited for up to 30 seconds.
    public static async Task<bool> EnsureServerAsync(int port, Action<string> info, Action<string> error)
    {
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is not null) return true;

        info(Lang.T($"AIT belum berjalan; menjalankannya di latar belakang ({client.BaseUrl}) ...", $"AIT is not running; starting it in the background ({client.BaseUrl}) ..."));
        try
        {
            SpawnTray(port);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error(Lang.T($"AIT tidak bisa dijalankan: {ex.Message}", $"AIT could not be started: {ex.Message}"));
            return false;
        }

        if (await WaitUntilAsync(async () => await client.GetInfoAsync() is not null, TimeSpan.FromSeconds(30))) return true;
        error(Lang.T($"AIT belum menjawab di {client.BaseUrl} setelah 30 detik. Log: {AppPaths.LogDir}", $"AIT did not answer at {client.BaseUrl} after 30 seconds. Log: {AppPaths.LogDir}"));
        return false;
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

    // Resume: -r/--resume, with the session id that follows it, or "" when none follows (pick from the list).
    public sealed record Options(int? Port, bool Open, bool Follow, IReadOnlyList<string> Rest, string? Error, bool Yes = false, string? Project = null,
        string? Prompt = null, bool Continue = false, string? Resume = null, bool Json = false, bool Detach = false, bool Start = false, int? Timeout = null)
    {
        public static Options Parse(string[] args)
        {
            int? port = null, timeout = null;
            bool open = false, follow = false, yes = false, resumeLast = false, json = false, detach = false, start = false;
            string? project = null, prompt = null, resume = null;
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
                    case "--project":
                        if (i + 1 >= args.Length) return new Options(null, false, false, rest, Lang.T("--project butuh nama project atau path folder.", "--project needs a project name or a folder path."));
                        project = args[++i];
                        break;
                    case "-p" or "--prompt":
                        if (i + 1 >= args.Length) return new Options(null, false, false, rest, Lang.T("-p butuh teks prompt.", "-p needs the prompt text."));
                        prompt = args[++i];
                        break;
                    case "-c" or "--continue": resumeLast = true; break;
                    case "-r" or "--resume":
                        resume = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : string.Empty;
                        break;
                    case "--json": json = true; break;
                    case "--detach": detach = true; break;
                    case "--start": start = true; break;
                    case "--timeout":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var seconds) || seconds is < 1 or > 3600)
                            return new Options(null, false, false, rest, Lang.T("--timeout butuh angka detik 1-3600.", "--timeout needs a number of seconds 1-3600."));
                        timeout = seconds;
                        break;
                    default: rest.Add(args[i]); break;
                }
            }

            return new Options(port, open, follow, rest, null, yes, project, prompt, resumeLast, resume, json, detach, start, timeout);
        }
    }
}

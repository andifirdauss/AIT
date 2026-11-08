using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.UI.Hosting.Chat;
using Microsoft.Extensions.Configuration;

namespace AIThread.UI.Hosting.Agent;

public sealed record DoctorFix(string Id, string Message, string? Command = null);

public sealed record DoctorFacts(bool LocalDbNeeded, bool LocalDbFound, bool ServerRunning, bool SystemReady, bool NodesReady, bool RouterReady, bool EngineInstalled, bool ProjectRegistered);

// aithread doctor: what this computer still needs before AIT can take a prompt, for a person or an AI agent. The next
// step is one fix at a time, in the order the steps depend on each other. --start also starts the server, except when
// its database engine is missing: the tray would only stop at an error dialog nobody asked for.
[SupportedOSPlatform("windows")]
public static class DoctorCommand
{
    public static async Task<int> RunAsync(int port, bool start, bool json)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var folder = Environment.CurrentDirectory;
        var node = Tool("node", "--version");
        var localDbNeeded = UsesLocalDb();
        var localDbFound = localDbNeeded && Tool("sqllocaldb", "info").Found;

        using var client = new ServerClient(port);
        var running = await client.GetInfoAsync() is not null;
        if (!running && start && (!localDbNeeded || localDbFound))
            running = await Cli.EnsureServerAsync(port, Console.Error.WriteLine, Console.Error.WriteLine);

        SetupStatusDto? setup = null;
        IReadOnlyList<CliBackendStatusDto> engines = [];
        ProjectDto? project = null;
        if (running)
        {
            using var api = new ChatApiClient(port);
            try
            {
                setup = await api.GetSetupStatusAsync(CancellationToken.None);
                engines = await api.GetCliEnginesAsync(CancellationToken.None);
                project = ProjectResolver.ByFolder(await api.GetProjectsAsync(CancellationToken.None), folder);
            }
            catch (ChatApiException ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        bool Done(string id) => setup?.Steps.FirstOrDefault(s => s.Id == id)?.Done == true;
        var fixes = FixesFor(new DoctorFacts(localDbNeeded, localDbFound, running, Done("system"), Done("nodes"), Done("router"), engines.Any(e => e.Installed), project is not null));
        var settings = SkillSettings.Load();

        if (json)
        {
            AgentOutput.Write(new
            {
                version = Cli.Version,
                url = client.BaseUrl,
                ready = fixes.Count == 0,
                folder,
                node = new { found = node.Found, version = node.Found ? node.Output : null },
                localDb = new { needed = localDbNeeded, found = localDbFound },
                server = new { running },
                setup = setup?.Steps.Select(s => new { s.Id, s.Done, s.Required, s.Detail }),
                engines = engines.Select(e => new { e.Tool, e.Installed, e.Version }),
                project = project is null ? null : new { project.Name, root = project.RootPath },
                skill = new { settings.Declined, disabled = settings.IsDisabled(project?.RootPath ?? folder) },
                fixes
            });
            return 0;
        }

        Console.WriteLine($"AIT {Cli.Version} · {client.BaseUrl}");
        Console.WriteLine($"  Node.js  : {(node.Found ? node.Output : Lang.T("tidak ditemukan", "not found"))}");
        if (localDbNeeded) Console.WriteLine($"  LocalDB  : {(localDbFound ? "OK" : Lang.T("tidak ditemukan", "not found"))}");
        Console.WriteLine($"  Server   : {(running ? Lang.T("berjalan", "running") : Lang.T("tidak berjalan", "not running"))}");
        foreach (var step in setup?.Steps ?? []) Console.WriteLine($"  {(step.Done ? "[x]" : "[ ]")} {step.Title}");
        Console.WriteLine($"  Project  : {project?.Name ?? Lang.T("folder ini belum terdaftar", "this folder is not registered")}");
        if (fixes.Count == 0) Console.WriteLine(Lang.T("Siap dipakai.", "Ready."));
        foreach (var fix in fixes) Console.WriteLine($"{Lang.T("Langkah berikutnya", "Next step")}: {fix.Message}{(fix.Command is null ? string.Empty : $" ({fix.Command})")}");
        return 0;
    }

    public static IReadOnlyList<DoctorFix> FixesFor(DoctorFacts facts)
    {
        if (facts.LocalDbNeeded && !facts.LocalDbFound)
            return [new DoctorFix("install-localdb", Lang.T(
                "Pasang SQL Server LocalDB: unduh SQL Server Express dari https://www.microsoft.com/sql-server/sql-server-downloads, jalankan installer-nya, pilih Download Media, lalu LocalDB. LocalDB juga ikut bersama Visual Studio. Atau isi ConnectionStrings:Default di %LOCALAPPDATA%\\AIThread\\appsettings.json ke SQL Server yang ada.",
                "Install SQL Server LocalDB: download SQL Server Express from https://www.microsoft.com/sql-server/sql-server-downloads, run its installer, choose Download Media, then LocalDB. LocalDB also comes with Visual Studio. Or set ConnectionStrings:Default in %LOCALAPPDATA%\\AIThread\\appsettings.json to an existing SQL Server."))];
        if (!facts.ServerRunning)
            return [new DoctorFix("start-server", Lang.T("Jalankan server AIT.", "Start the AIT server."), "aithread doctor --start --json")];
        if (!facts.SystemReady)
            return [new DoctorFix("database", Lang.T("Database atau short memory AIT belum tersambung. Lihat halaman Setup.", "AIT's database or short memory is not connected. See the Setup page."), "aithread setup")];
        if (!facts.NodesReady)
            return facts.EngineInstalled
                ? [new DoctorFix("setup-nodes", Lang.T("Buat node dari CLI resmi yang terpasang.", "Create the nodes from the installed official CLI."), "aithread setup auto --json")]
                : [new DoctorFix("no-model", Lang.T("Belum ada model. Login ke CLI resmi (Claude Code, Gemini CLI, Codex) atau hubungkan 9router di halaman Setup.", "No model yet. Sign in to an official CLI (Claude Code, Gemini CLI, Codex) or connect 9router on the Setup page."), "aithread setup")];
        if (!facts.RouterReady)
            return [new DoctorFix("router", Lang.T("Node memakai 9router yang belum tersambung. Hubungkan di halaman Setup; API key diketik di sana.", "The nodes use 9router, which is not connected. Connect it on the Setup page; the API key is typed there."), "aithread setup")];
        if (!facts.ProjectRegistered)
            return [new DoctorFix("register-project", Lang.T("Daftarkan folder ini sebagai project.", "Register this folder as a project."), "aithread project add --json")];
        return [];
    }

    private static (bool Found, string Output) Tool(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return (false, string.Empty);

            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return (false, string.Empty);
            }
            return (process.ExitCode == 0, output.GetAwaiter().GetResult().Trim());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (false, string.Empty);
        }
    }

    // The appsettings.json layers of ServerHost; an unreadable file means the default, LocalDB.
    private static bool UsesLocalDb()
    {
        try
        {
            var connection = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
                .AddJsonFile(AppPaths.UserSettingsFile, optional: true)
                .Build()
                .GetConnectionString("Default");
            return connection is null || connection.Contains("localdb", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or JsonException or IOException)
        {
            return true;
        }
    }
}

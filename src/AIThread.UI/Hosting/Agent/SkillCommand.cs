using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.UI.Hosting.Chat;

namespace AIThread.UI.Hosting.Agent;

// aithread skill on|off [path]: the developer's way out of the plugin's enforcement. It works without a server; with one
// running, the folder is widened to its registered project.
[SupportedOSPlatform("windows")]
public static class SkillCommand
{
    private static string Usage => Lang.T("Pemakaian: AIThread.exe skill on|off [path] | skill install|uninstall <codex|gemini|opencode|qwen|omp|all>",
        "Usage: AIThread.exe skill on|off [path] | skill install|uninstall <codex|gemini|opencode|qwen|omp|all>");

    public static async Task<int> RunAsync(int port, IReadOnlyList<string> rest)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var action = rest.FirstOrDefault()?.ToLowerInvariant();
        if (action is "install" or "uninstall" && rest.Count == 2) return Install(action, rest[1].ToLowerInvariant());
        if (rest.Count is 0 or > 2 || action is not ("on" or "off"))
        {
            Console.Error.WriteLine(Usage);
            return ChatCommand.BadArguments;
        }

        var folder = await ProjectRootOfAsync(port, Path.GetFullPath(rest.Count == 2 ? rest[1] : Environment.CurrentDirectory));
        var settings = SkillSettings.Load();
        if (action == "on") settings.Enable(folder);
        else settings.Disable(folder);
        settings.Save();

        Console.WriteLine(action == "on"
            ? Lang.T($"Plugin AIT aktif untuk {folder}.", $"The AIT plugin is on for {folder}.")
            : Lang.T($"Plugin AIT dimatikan untuk {folder}.", $"The AIT plugin is off for {folder}."));
        return ChatCommand.Ok;
    }

    // "all" means the CLIs that have been used on this computer. It works without a server, so 'npx aithread skill install'
    // is enough: the skill installs AIT itself on the first prompt that changes code.
    private static int Install(string action, string target)
    {
        var installer = new SkillInstaller(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppPaths.HomeDir);
        if (target != "all" && !SkillInstaller.Clis.Contains(target))
        {
            Console.Error.WriteLine(Usage);
            return ChatCommand.BadArguments;
        }

        var clis = target == "all" ? SkillInstaller.Clis.Where(installer.IsPresent).ToList() : [target];
        if (clis.Count == 0)
        {
            Console.Error.WriteLine(Lang.T("Tidak ada CLI yang dikenal di komputer ini.", "No known CLI on this computer."));
            return ChatCommand.Failed;
        }

        var exitCode = ChatCommand.Ok;
        foreach (var cli in clis)
        {
            try
            {
                var result = action == "install" ? installer.Install(cli) : installer.Uninstall(cli);
                Console.WriteLine(action == "install" ? Lang.T($"{cli}: terpasang.", $"{cli}: installed.") : Lang.T($"{cli}: dilepas.", $"{cli}: removed."));
                foreach (var file in result.Files) Console.WriteLine($"  {file}");
                if (result.Note is { } note) Console.WriteLine($"  ! {note}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AppValidationException)
            {
                Console.Error.WriteLine($"{cli}: {ex.Message}");
                exitCode = ChatCommand.Failed;
            }
        }
        return exitCode;
    }

    private static async Task<string> ProjectRootOfAsync(int port, string folder)
    {
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is null) return folder;

        using var api = new ChatApiClient(port);
        try
        {
            return ProjectResolver.ByFolder(await api.GetProjectsAsync(CancellationToken.None), folder)?.RootPath ?? folder;
        }
        catch (ChatApiException)
        {
            return folder;
        }
    }
}

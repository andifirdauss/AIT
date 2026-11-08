using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.Localization;
using AIThread.UI.Hosting.Chat;

namespace AIThread.UI.Hosting.Agent;

// aithread project add [path]: registers a folder. The server refuses a drive, the home folder and system folders.
[SupportedOSPlatform("windows")]
public static class ProjectCommand
{
    private static string Usage => Lang.T("Pemakaian: AIThread.exe project add [path] [--json]", "Usage: AIThread.exe project add [path] [--json]");

    public static async Task<int> RunAsync(int port, IReadOnlyList<string> rest, bool json)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (rest.Count is 0 or > 2 || !rest[0].Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(Usage);
            return ChatCommand.BadArguments;
        }

        if (!await Cli.EnsureServerAsync(port, Console.Error.WriteLine, Console.Error.WriteLine)) return ChatCommand.Failed;

        using var api = new ChatApiClient(port);
        try
        {
            var project = await api.RegisterProjectAsync(Path.GetFullPath(rest.Count == 2 ? rest[1] : Environment.CurrentDirectory), CancellationToken.None);
            if (json) AgentOutput.Write(new { project.Id, project.Name, root = project.RootPath, project.Stacks });
            else Console.WriteLine(Lang.T($"Project terdaftar: {project.Name} ({project.RootPath})", $"Project registered: {project.Name} ({project.RootPath})"));
            return ChatCommand.Ok;
        }
        catch (ChatApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ChatCommand.Failed;
        }
    }
}

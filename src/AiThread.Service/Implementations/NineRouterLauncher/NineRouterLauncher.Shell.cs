using System.Diagnostics;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
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
}

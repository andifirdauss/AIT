using System.Diagnostics;
using System.Text;
using AIThread.Service.Verification;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class CommandRunner
{
    // The shell, environment and redirection of a planner command; shared by run_command and the background processes
    // of start_process, so a command behaves the same in both. shell: "bash" or "cmd.exe".
    internal static ProcessStartInfo StartInfo(string command, string workingDirectory, out string shell)
    {
        var bash = Bash.Value;
        var startInfo = new ProcessStartInfo(bash ?? "cmd.exe")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (bash is not null)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            // cmd.exe parses its own command line; the command goes in as written.
            startInfo.Arguments = $"/d /s /c \"{command}\"";
        }

        BuildRunner.PrepareEnvironment(startInfo.Environment);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["TERM"] = "dumb";
        // Git Bash keeps the Windows PATH, so curl, node, dotnet and the rest are found as in a terminal.
        if (bash is not null) startInfo.Environment["MSYS_NO_PATHCONV"] = "1";

        shell = bash is null ? "cmd.exe" : "bash";
        return startInfo;
    }
}

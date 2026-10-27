namespace AIThread.Service.Workspace.Commands;

public sealed partial class CommandRunner
{
    // Git Bash next to the git on PATH (C:\Program Files\Git\cmd\git.exe → ..\bin\bash.exe); never the WSL bash.exe of
    // System32, which runs in another file system. Elsewhere /bin/bash.
    private static string? FindBash()
    {
        if (!OperatingSystem.IsWindows()) return File.Exists("/bin/bash") ? "/bin/bash" : null;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string git;
            try
            {
                git = Path.Combine(directory, "git.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (!File.Exists(git)) continue;

            // git.exe sits in Git\cmd, Git\bin or Git\mingw64\bin.
            for (var root = Path.GetDirectoryName(directory); root is not null; root = Path.GetDirectoryName(root))
            {
                foreach (var candidate in new[] { Path.Combine(root, "bin", "bash.exe"), Path.Combine(root, "usr", "bin", "bash.exe") })
                    if (File.Exists(candidate)) return candidate;
                if (File.Exists(Path.Combine(root, "git-bash.exe")) || Path.GetFileName(root).Equals("Git", StringComparison.OrdinalIgnoreCase)) break;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installed = Path.Combine(programFiles, "Git", "bin", "bash.exe");
        return File.Exists(installed) ? installed : null;
    }
}

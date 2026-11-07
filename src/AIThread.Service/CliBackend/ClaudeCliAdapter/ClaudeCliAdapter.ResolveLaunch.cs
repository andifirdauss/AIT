namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The configured path, else claude on PATH, else the native installer's %USERPROFILE%\.local\bin\claude(.exe), else
    // the npm shim (claude.cmd) resolved to the package's own entry.
    private CliLaunch? ResolveLaunch() =>
        CliProcess.Resolve(options.Value.ClaudeCli.Path, Command, NpmPackage,
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")]);
}

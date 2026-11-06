namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The configured path, else claude on PATH, else the native installer's %USERPROFILE%\.local\bin\claude.exe, else
    // the npm shim (claude.cmd), which is resolved to the package's own entry: a .cmd would need cmd.exe and its quoting.
    private Launch? ResolveLaunch()
    {
        var configured = options.Value.ClaudeCli.Path;
        if (!string.IsNullOrWhiteSpace(configured))
            return FromFile(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));

        var localBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
        if (!OperatingSystem.IsWindows())
        {
            var unix = FindOnPath("claude") ?? Existing(Path.Combine(localBin, "claude"));
            return unix is null ? null : new Launch(unix, []);
        }

        var exe = FindOnPath("claude.exe") ?? Existing(Path.Combine(localBin, "claude.exe"));
        if (exe is not null) return new Launch(exe, []);

        var shim = FindOnPath("claude.cmd")
                   ?? Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"));
        return shim is null ? null : FromNpmShim(shim);
    }
}

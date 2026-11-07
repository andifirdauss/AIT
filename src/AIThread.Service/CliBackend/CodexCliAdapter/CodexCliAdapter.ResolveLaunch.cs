namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // The configured path, else codex on PATH (standalone binary or npm link), else the npm shim (codex.cmd) resolved
    // to the package's own entry.
    private CliLaunch? ResolveLaunch() => CliProcess.Resolve(options.Value.CodexCli.Path, Command, NpmPackage);
}

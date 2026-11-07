namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // The configured path, else gemini on PATH, else the npm shim (gemini.cmd) resolved to the package's own entry.
    private CliLaunch? ResolveLaunch() => CliProcess.Resolve(options.Value.GeminiCli.Path, Command, NpmPackage);
}

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // The configured path, else agy on PATH, else the installer's %LOCALAPPDATA%\agy\bin\agy(.exe).
    private CliLaunch? ResolveLaunch() =>
        CliProcess.Resolve(options.Value.AntigravityCli.Path, Command, string.Empty,
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin")]);
}

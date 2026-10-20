namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private NineRouterCli? FindCli(IReadOnlyList<string> dirs) =>
        NineRouterLocator.FindCli(Settings.CliPath, dirs, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory, File.Exists);
}

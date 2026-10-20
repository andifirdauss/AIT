namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // The node.exe NineRouterLauncher would run 9router with.
    private string? FindNodeJs() =>
        NineRouterLocator.FindExecutable("node.exe", options.Value.NineRouter.NodePath, NineRouterLocator.SearchDirs(), File.Exists);
}

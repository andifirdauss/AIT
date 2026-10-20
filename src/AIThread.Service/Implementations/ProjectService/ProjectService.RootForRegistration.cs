using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // Registering a folder by hand is explicit: the folder's own markers win, then the project it belongs to, and a
    // folder without any marker is taken as it is (only drives, home and system folders are refused).
    private static ProjectRoot RootForRegistration(string path)
    {
        var directory = FolderOf(path.Trim().Trim('"')) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");
        if (ProjectLocator.IsBlockedRoot(directory))
            throw new AppValidationException("Folder ini terlalu luas untuk jadi project (drive, home, atau folder sistem). Pilih folder project-nya.");

        return ProjectLocator.Inspect(directory) ?? ProjectLocator.Resolve(directory).Root ?? ProjectLocator.Plain(directory);
    }
}

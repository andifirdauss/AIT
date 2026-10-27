using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    // Snapshot paths come from WorkspaceScope.ToDisplay: relative ones stay inside the root (checked again anyway);
    // absolute ones are files outside the root the developer allowed the thread to change, recorded by the thread itself.
    private static string FullPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) return Path.GetFullPath(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException(Lang.T($"Path '{relativePath}' berada di luar project.", $"Path '{relativePath}' is outside the project."));
        return fullPath;
    }
}

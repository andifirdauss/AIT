using AIThread.Service.Models;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    // Every project file as it is now (relative, forward slashes), outside build output and third-party code.
    private static IReadOnlyList<string> Files(ProjectContext project, CancellationToken cancellationToken)
    {
        var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
        var files = new List<string>();
        foreach (var full in scope.EnumerateFiles(maxFiles: MaxFiles))
        {
            if (files.Count % 1_000 == 0) cancellationToken.ThrowIfCancellationRequested();
            files.Add(scope.ToRelative(full));
        }
        var thirdParty = ThirdPartyFilter.Load(scope.Root, files);
        return files.Where(f => !thirdParty.IsThirdParty(f)).ToList();
    }
}

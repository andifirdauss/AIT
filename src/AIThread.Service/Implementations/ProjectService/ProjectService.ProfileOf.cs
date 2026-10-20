using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // Stacks and modules come from the manifests on disk; a short cache keeps the project list and every new thread
    // from walking the folder tree again.
    private ProjectProfile ProfileOf(string rootPath)
    {
        var now = DateTime.UtcNow;
        if (_profiles.TryGetValue(rootPath, out var cached) && cached.ExpiresAt > now) return cached;

        var manifests = ProjectLocator.FindManifests(rootPath);
        var profile = new ProjectProfile(ProjectLocator.StacksOf(manifests), manifests.Select(m => m.RelativePath).ToList(), now + ProfileLifetime);
        _profiles[rootPath] = profile;
        return profile;
    }
}

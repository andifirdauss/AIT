using AIThread.Common.Entities;
using AIThread.Service.Business;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // This clone's remote branches as environments, the most recently changed first: name = the branch ("develop",
    // "feature/login"), ref = "origin/develop". A branch on several remotes is taken from origin. Remote HEAD aliases and
    // names an environment cannot hold (too long, the working wiki's name) are left out.
    private static async Task<IReadOnlyList<EnvironmentRef>> RemoteBranchesAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || !GitTools.IsRepository(root)) return [];
        var result = await GitProcess.RunAsync(root, ["for-each-ref", "--sort=-committerdate", "--format=%(refname)%09%(objectname)", "refs/remotes"],
            cancellationToken);
        if (!result.Ok) return [];

        const string prefix = "refs/remotes/";
        var branches = new List<EnvironmentRef>();
        foreach (var line in GitProcess.Lines(result.Output))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || !parts[0].StartsWith(prefix, StringComparison.Ordinal)) continue;
            var reference = parts[0][prefix.Length..];
            var slash = reference.IndexOf('/');
            if (slash <= 0 || slash == reference.Length - 1) continue;
            var remote = reference[..slash];
            var branch = reference[(slash + 1)..];
            if (branch == "HEAD" || branch.Length > ProjectEnvironment.MaxNameLength || !RefPattern().IsMatch(reference)
                || string.Equals(branch, BusinessEnvService.Working, StringComparison.OrdinalIgnoreCase)) continue;

            var taken = branches.FindIndex(b => string.Equals(b.Name, branch, StringComparison.OrdinalIgnoreCase));
            var environment = new EnvironmentRef(Guid.Empty, branch, reference, 0, false, true, parts[1]);
            if (taken < 0) branches.Add(environment);
            else if (remote == "origin" && !branches[taken].Ref.StartsWith("origin/", StringComparison.Ordinal)) branches[taken] = environment;
        }
        return branches.Take(MaxRemoteBranches).ToList();
    }
}

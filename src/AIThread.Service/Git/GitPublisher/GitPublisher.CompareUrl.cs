namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // The page that opens a pull/merge request from head into base on GitHub, GitLab, Bitbucket or Azure DevOps (hosts
    // recognized by name, self-hosted GitHub Enterprise / GitLab included); null for another host or a local remote.
    public static string? CompareUrl(string? remoteUrl, string baseBranch, string headBranch)
    {
        if (RemoteWeb(remoteUrl) is not { } web) return null;
        var (host, path) = web;
        var site = $"https://{host}/{path}";
        if (host.Contains("github", StringComparison.Ordinal))
            return $"{site}/compare/{Ref(baseBranch)}...{Ref(headBranch)}?expand=1";
        if (host.Contains("gitlab", StringComparison.Ordinal))
            return $"{site}/-/merge_requests/new?merge_request[source_branch]={Uri.EscapeDataString(headBranch)}&merge_request[target_branch]={Uri.EscapeDataString(baseBranch)}";
        if (host.Contains("bitbucket", StringComparison.Ordinal))
            return $"{site}/pull-requests/new?source={Uri.EscapeDataString(headBranch)}&dest={Uri.EscapeDataString(baseBranch)}";
        if (host is "dev.azure.com" || host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
            return $"{site}/pullrequestcreate?sourceRef={Uri.EscapeDataString(headBranch)}&targetRef={Uri.EscapeDataString(baseBranch)}";
        return null;

        // A branch in a path keeps its slashes (feature/x), each part escaped.
        static string Ref(string branch) => string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));
    }
}

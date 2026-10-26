using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // git fetch of every remote, so other developers' pushes and merges show here (Pushed now, environments later). The
    // only git command of AIT that writes (remote-tracking refs); never asks for credentials: a remote that needs them
    // just fails, said once in the activity log. The project's threads are looked at again afterwards.
    public async Task<GitFetchStatus?> FetchAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        string? root;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            root = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.RootPath).FirstOrDefaultAsync(cancellationToken);
        if (root is null || !Directory.Exists(root) || !GitTools.IsRepository(root)) return null;

        var remotes = await GitProcess.RunAsync(root, ["remote"], cancellationToken);
        if (!remotes.Ok || GitProcess.Lines(remotes.Output).Count == 0) return null;

        // Empty input closes stdin: nothing can wait for a passphrase.
        var result = await GitProcess.RunAsync(root, ["fetch", "--all", "--prune", "--quiet"], cancellationToken, input: string.Empty, timeout: FetchTimeout);
        var status = new GitFetchStatus(DateTime.UtcNow, result.Ok ? null : Clip(result.Error, 500));
        var before = _fetches.TryGetValue(projectId, out var last) ? last : null;
        _fetches[projectId] = status;

        if (status.Error is not null && before?.Error is null)
            activityLog.Warn("git", Lang.T($"git fetch gagal: {status.Error}", $"git fetch failed: {status.Error}"), projectId: projectId);
        else if (status.Error is null && before?.Error is not null)
            activityLog.Success("git", Lang.T("git fetch berhasil lagi.", "git fetch works again."), projectId: projectId);

        if (status.Error is null) await RefreshProjectAsync(projectId, cancellationToken);
        return status;
    }
}

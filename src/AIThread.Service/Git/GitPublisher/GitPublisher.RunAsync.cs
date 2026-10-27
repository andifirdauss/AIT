using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Per repository: create the new branch (when asked), stage and commit the job's files only (git commit -- <files>
    // leaves anything else the developer staged alone), push the branch, then open the pull request. A failure stops
    // that repository and is reported with git's message; a pull request that cannot be opened leaves a link instead.
    public async Task<PublishOutcome> RunAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        var succeeded = true;
        foreach (var repo in job.Repos)
        {
            var name = Path.GetFileName(repo.Root);
            if (repo.NewBranch is { } newBranch)
            {
                var switched = await SwitchBranchAsync(repo.Root, newBranch, cancellationToken);
                if (!switched.Ok)
                {
                    succeeded = false;
                    lines.Add(Lang.T($"❌ Branch `{newBranch}` di **{name}** gagal dibuat: {Clip(switched.Error)}", $"❌ Branch `{newBranch}` in **{name}** could not be created: {Clip(switched.Error)}"));
                    continue;
                }
                lines.Add(Lang.T($"✅ Branch baru `{newBranch}` di **{name}** (dari {repo.Branch})", $"✅ New branch `{newBranch}` in **{name}** (from {repo.Branch})"));
            }

            if (job.Commit && repo.Files.Count > 0)
            {
                var add = await GitProcess.RunAsync(repo.Root, ["add", "--", .. repo.Files], cancellationToken, timeout: WriteTimeout);
                var commit = add.Ok
                    ? await GitProcess.RunAsync(repo.Root, ["commit", "-F", "-", "--", .. repo.Files], cancellationToken, input: job.Message ?? "Update", timeout: WriteTimeout)
                    : add;
                if (!commit.Ok)
                {
                    succeeded = false;
                    lines.Add(Lang.T($"❌ Commit di **{name}** gagal: {Clip(commit.Error)}", $"❌ Commit in **{name}** failed: {Clip(commit.Error)}"));
                    continue;
                }
                var head = (await GitProcess.RunAsync(repo.Root, ["rev-parse", "--short", "HEAD"], cancellationToken)).Output.Trim();
                lines.Add(Lang.T($"✅ Commit `{head}` di **{name}** ({repo.Target}): {repo.Files.Count} file", $"✅ Commit `{head}` in **{name}** ({repo.Target}): {repo.Files.Count} file(s)"));
            }

            if (job.Push && repo.Remote is { } remote)
            {
                string[] args = repo.HasUpstream ? ["push", remote, repo.Target] : ["push", "--set-upstream", remote, repo.Target];
                var push = await GitProcess.RunAsync(repo.Root, args, cancellationToken, timeout: WriteTimeout);
                if (!push.Ok)
                {
                    succeeded = false;
                    lines.Add(Lang.T($"❌ Push **{name}** ke {remote}/{repo.Target} gagal: {Clip(push.Error)}", $"❌ Push of **{name}** to {remote}/{repo.Target} failed: {Clip(push.Error)}"));
                    continue;
                }
                lines.Add(Lang.T($"✅ Push **{name}** ke {remote}/{repo.Target}", $"✅ Pushed **{name}** to {remote}/{repo.Target}"));

                if (job.PullRequest is { } pullRequest && repo.Base is not null)
                    lines.Add(await OpenPullRequestAsync(repo, pullRequest, cancellationToken));
            }
        }
        return new PublishOutcome(succeeded, lines);
    }
}

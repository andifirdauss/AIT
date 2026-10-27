using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Per repository: stage and commit the job's files only (git commit -- <files> leaves anything else the developer
    // staged alone), then push the branch. A failure stops that repository and is reported with git's message.
    public async Task<PublishOutcome> RunAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        var succeeded = true;
        foreach (var repo in job.Repos)
        {
            var name = Path.GetFileName(repo.Root);
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
                lines.Add(Lang.T($"✅ Commit `{head}` di **{name}** ({repo.Branch}): {repo.Files.Count} file", $"✅ Commit `{head}` in **{name}** ({repo.Branch}): {repo.Files.Count} file(s)"));
            }

            if (job.Push && repo.Remote is { } remote)
            {
                string[] args = repo.HasUpstream ? ["push", remote, repo.Branch] : ["push", "--set-upstream", remote, repo.Branch];
                var push = await GitProcess.RunAsync(repo.Root, args, cancellationToken, timeout: WriteTimeout);
                if (!push.Ok)
                {
                    succeeded = false;
                    lines.Add(Lang.T($"❌ Push **{name}** ke {remote}/{repo.Branch} gagal: {Clip(push.Error)}", $"❌ Push of **{name}** to {remote}/{repo.Branch} failed: {Clip(push.Error)}"));
                    continue;
                }
                lines.Add(Lang.T($"✅ Push **{name}** ke {remote}/{repo.Branch}", $"✅ Pushed **{name}** to {remote}/{repo.Branch}"));
            }
        }
        return new PublishOutcome(succeeded, lines);
    }
}

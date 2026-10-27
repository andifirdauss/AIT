using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Opens the pull/merge request of a pushed branch with gh (GitHub) or glab (GitLab) when installed; otherwise, or
    // when that fails, the line carries the link that opens it. Never fails the publish: the push already happened.
    private async Task<string> OpenPullRequestAsync(PublishRepo repo, PublishPullRequest request, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(repo.Root);
        var head = repo.Target;
        var baseBranch = repo.Base!;
        var remoteUrl = (await GitProcess.RunAsync(repo.Root, ["remote", "get-url", repo.Remote!], cancellationToken)).Output.Trim();
        var host = RemoteWeb(remoteUrl)?.Host ?? string.Empty;

        (string Tool, string[] Arguments)? cli = host.Contains("github", StringComparison.Ordinal)
            ? ("gh", ["pr", "create", "--base", baseBranch, "--head", head, "--title", request.Title, "--body-file", "-"])
            : host.Contains("gitlab", StringComparison.Ordinal)
                ? ("glab", ["mr", "create", "--source-branch", head, "--target-branch", baseBranch, "--title", request.Title, "--description-file", "-", "--yes"])
                : null;

        string reasonId, reasonEn;
        if (cli is { } command && _findTool(command.Tool) is { } executable)
        {
            var tool = command.Tool;
            var created = await ForgeCli.RunAsync(executable, repo.Root, command.Arguments, request.Body ?? string.Empty, WriteTimeout, cancellationToken);
            if (created.Ok)
            {
                var url = created.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? created.Output.Trim();
                return Lang.T($"✅ Pull request **{name}** ({head} → {baseBranch}) dibuat: {url}", $"✅ Pull request for **{name}** ({head} → {baseBranch}) opened: {url}");
            }
            reasonId = $"{tool} gagal: {Clip(created.Error)}";
            reasonEn = $"{tool} failed: {Clip(created.Error)}";
        }
        else if (cli?.Tool is { } missing)
        {
            reasonId = $"{missing} tidak terpasang";
            reasonEn = $"{missing} is not installed";
        }
        else
        {
            reasonId = "hosting git tidak dikenali";
            reasonEn = "the git host is not recognized";
        }

        return CompareUrl(remoteUrl, baseBranch, head) is { } link
            ? Lang.T($"🔗 Buka pull request **{name}** ({head} → {baseBranch}): {link} — {reasonId}", $"🔗 Open the pull request for **{name}** ({head} → {baseBranch}): {link} — {reasonEn}")
            : Lang.T($"⚠️ Pull request **{name}** tidak bisa dibuka otomatis ({reasonId}); buka dari `{head}` ke `{baseBranch}` di hosting git Anda.",
                $"⚠️ The pull request for **{name}** could not be opened automatically ({reasonEn}); open it from `{head}` into `{baseBranch}` on your git host.");
    }
}

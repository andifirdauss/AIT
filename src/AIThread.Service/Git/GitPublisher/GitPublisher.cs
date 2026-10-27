using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Git;

// One repository's part of a commit/push: the thread's files in it (relative to the repository), the branch checked
// out and the remote it goes to. NewBranch: the branch created before the commit (then committed and pushed instead of
// Branch); Base: the branch a pull request merges into.
public sealed record PublishRepo(string Root, string Branch, string? Remote, bool HasUpstream, IReadOnlyList<string> Files,
    string? NewBranch = null, string? Base = null)
{
    // The branch that gets the commit and the push.
    public string Target => NewBranch ?? Branch;
}

// The pull/merge request opened after the push; Base empty = the remote's default branch.
public sealed record PublishPullRequest(string Title, string? Body = null, string? Base = null);

// What the developer asked for (ThreadPlan.Publish).
public sealed record PublishRequest(bool Commit, bool Push, string? Message, string? Branch = null, PublishPullRequest? PullRequest = null);

// What AIT commits and pushes for a thread, prepared before the developer is asked. Items are the approval targets:
// a new branch and a pull request per repository first, then a commit per repository with files to commit, a push per
// repository.
public sealed record PublishJob(bool Commit, bool Push, string? Message, IReadOnlyList<PublishRepo> Repos, string? Branch = null,
    PublishPullRequest? PullRequest = null)
{
    public IReadOnlyList<ApprovalItem> Items =>
    [
        .. Repos.Where(r => r.NewBranch is not null).Select(r => new ApprovalItem(ApprovalKind.Branch, ApprovalTargets.Normalize(ApprovalKind.Branch, $"{r.Root}|{r.NewBranch}"),
            Lang.T($"dari {r.Branch}", $"from {r.Branch}"))),
        .. PullRequest is { } request
            ? Repos.Where(r => r.Remote is not null && r.Base is not null).Select(r => new ApprovalItem(ApprovalKind.PullRequest,
                ApprovalTargets.Normalize(ApprovalKind.PullRequest, $"{r.Root}|{r.Base}<-{r.Target}"), $"\"{FirstLine(request.Title)}\""))
            : [],
        .. Commit ? Repos.Where(r => r.Files.Count > 0).Select(r => new ApprovalItem(ApprovalKind.Commit, ApprovalTargets.Normalize(ApprovalKind.Commit, r.Root),
            Lang.T($"{r.Files.Count} file · \"{FirstLine(Message)}\"", $"{r.Files.Count} file(s) · \"{FirstLine(Message)}\""))) : [],
        .. Push ? Repos.Where(r => r.Remote is not null).Select(r => new ApprovalItem(ApprovalKind.Push, ApprovalTargets.Normalize(ApprovalKind.Push, $"{r.Root}|{r.Remote}/{r.Target}"),
            r.HasUpstream ? null : Lang.T("branch baru di remote (upstream diset)", "new branch on the remote (upstream is set)"))) : []
    ];

    // A push to main, master, production or a release branch is allowed one time at a time.
    public bool PushesProtectedBranch => Push && Repos.Any(r => GitPublisher.IsProtectedBranch(r.Target));

    private static string FirstLine(string? text) => (text ?? string.Empty).Split('\n')[0].Trim();
}

// Result of one repository's commit and/or push, as lines for the answer. Committed: the new commit, short id.
public sealed record PublishOutcome(bool Succeeded, IReadOnlyList<string> Lines);

// Commits and pushes only what the developer allowed: the files a thread wrote (never git add -A), never with --force,
// never waiting for credentials (a push that needs a login fails with git's message). A new branch is only ever
// created, never reset or reused; a pull request goes through gh/glab when installed, otherwise the answer carries the
// link to open it. findTool: where a CLI (gh, glab) lives, null when it is not installed (tests replace it).
public sealed partial class GitPublisher(Func<string, string?>? findTool = null) : IGitPublisher
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(2);

    private readonly Func<string, string?> _findTool = findTool ?? ForgeCli.Find;
}

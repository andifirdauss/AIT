using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Git;

// One repository's part of a commit/push: the thread's files in it (relative to the repository), the branch checked
// out and the remote it goes to.
public sealed record PublishRepo(string Root, string Branch, string? Remote, bool HasUpstream, IReadOnlyList<string> Files);

// What AIT commits and pushes for a thread, prepared before the developer is asked. Items are the approval targets:
// a commit per repository with files to commit, a push per repository.
public sealed record PublishJob(bool Commit, bool Push, string? Message, IReadOnlyList<PublishRepo> Repos)
{
    public IReadOnlyList<ApprovalItem> Items =>
    [
        .. Commit ? Repos.Where(r => r.Files.Count > 0).Select(r => new ApprovalItem(ApprovalKind.Commit, ApprovalTargets.Normalize(ApprovalKind.Commit, r.Root),
            Lang.T($"{r.Files.Count} file · \"{FirstLine(Message)}\"", $"{r.Files.Count} file(s) · \"{FirstLine(Message)}\""))) : [],
        .. Push ? Repos.Where(r => r.Remote is not null).Select(r => new ApprovalItem(ApprovalKind.Push, ApprovalTargets.Normalize(ApprovalKind.Push, $"{r.Root}|{r.Remote}/{r.Branch}"),
            r.HasUpstream ? null : Lang.T("branch baru di remote (upstream diset)", "new branch on the remote (upstream is set)"))) : []
    ];

    // A push to main, master, production or a release branch is allowed one time at a time.
    public bool PushesProtectedBranch => Push && Repos.Any(r => GitPublisher.IsProtectedBranch(r.Branch));

    private static string FirstLine(string? text) => (text ?? string.Empty).Split('\n')[0].Trim();
}

// Result of one repository's commit and/or push, as lines for the answer. Committed: the new commit, short id.
public sealed record PublishOutcome(bool Succeeded, IReadOnlyList<string> Lines);

// Commits and pushes only what the developer allowed: the files a thread wrote (never git add -A), never with --force,
// never waiting for credentials (a push that needs a login fails with git's message).
public sealed partial class GitPublisher : IGitPublisher
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(2);
}

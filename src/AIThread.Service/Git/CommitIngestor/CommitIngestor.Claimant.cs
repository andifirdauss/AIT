using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // "name@machine": the AIT that took a commit on (only it hands the commit to its business wiki writer).
    public static string Claimant() => $"{GitIdentity.UserName ?? Environment.UserName}@{Environment.MachineName}";
}

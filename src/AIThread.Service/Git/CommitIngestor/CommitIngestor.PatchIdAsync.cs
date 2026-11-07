using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // git patch-id --stable of the commit's whole diff (not only below the project root, so every clone gets the same
    // one): equal for a rebase or cherry-pick of the same change. Null when git cannot tell or the patch is too large.
    private static async Task<string?> PatchIdAsync(string root, string sha, CancellationToken cancellationToken)
    {
        var show = await GitProcess.RunAsync(root, ["show", "--format=", "--patch", "--no-color", "--no-ext-diff", "--no-renames", sha], cancellationToken);
        if (!show.Ok || show.Output.Length == 0 || show.Output.Length > MaxPatchChars) return null;
        var patch = await GitProcess.RunAsync(root, ["patch-id", "--stable"], cancellationToken, input: show.Output);
        var id = patch.Ok ? patch.Output.Split(' ', 2)[0].Trim() : string.Empty;
        return id.Length is > 0 and <= Data.Memory.MemorySchema.GitCommitLength ? id : null;
    }
}

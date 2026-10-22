using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    // The real values in place of the placeholders the executor copied, matched against the file as it is now (its
    // secrets are learned first, so this works after a restart too). Unknown: placeholders AIT cannot map back.
    private (IReadOnlyList<EditBlock> Blocks, IReadOnlyList<string> Unknown) RestoreSecrets(ExecutorJob job, TextFileState current, IReadOnlyList<EditBlock> blocks)
    {
        if (current.Exists) secrets.Learn(job.Task.File, current.Content, job.Scope.Secrets.Keys);
        var unknown = new List<string>();
        var restored = new List<EditBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var search = secrets.Restore(block.Search);
            var replace = secrets.Restore(block.Replace);
            unknown.AddRange(search.Unknown);
            unknown.AddRange(replace.Unknown);
            restored.Add(search.Restored + replace.Restored == 0 ? block : block with { Search = search.Text, Replace = replace.Text });
        }
        return (restored, unknown);
    }
}

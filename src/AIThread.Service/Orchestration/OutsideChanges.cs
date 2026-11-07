using System.Text;
using AIThread.Service.Git;

namespace AIThread.Service.Orchestration;

// The "# Uncommitted changes made outside AIT" section of the planner's brief (GitChangeTracker.OutsideChangesAsync):
// files of the working tree someone changed without AIT — by hand, in an IDE, or with another CLI talking to its
// provider directly — so no AIT thread and no memory knows of them yet. For the model: English.
public static class OutsideChanges
{
    public static string Brief(OutsideChangeSet changes)
    {
        var builder = new StringBuilder("# Uncommitted changes made outside AIT (git working tree vs HEAD, files no AIT thread wrote or changed after one)\n");
        foreach (var file in changes.Files)
        {
            builder.Append("- ").Append(file.Path).Append(" (").Append(file.Status);
            if (file.Added is { } added) builder.Append($", +{added}");
            if (file.Removed is { } removed) builder.Append($" -{removed}");
            if (file.AfterAit) builder.Append(", edited after an AIT thread wrote it");
            builder.Append(")\n");
        }
        if (changes.Total > changes.Files.Count) builder.Append($"- … {changes.Total - changes.Files.Count} more\n");
        return builder.Append("Memory does not know these changes yet: when the request touches one of these files, read its current content before relying on memory, ")
            .Append("and keep the developer's edits in it. They are not \"this change\" of the session unless the request says so.\n")
            .ToString();
    }
}

using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class FileOperations
{
    // Signatures only: the bodies did not change, and a full diff of a large split would bury the reviewer.
    private static string ExtractDiff(string source, string target, bool targetExisted, MemberMoveResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append($"--- a/{source}\n+++ b/{source}\n@@ members removed, bodies moved unchanged to {target} @@\n");
        foreach (var signature in result.Signatures) builder.Append($"-{signature}\n");
        if (result.Deduplicated.Count > 0) builder.Append($"@@ already declared in {target}, only removed here: {string.Join(", ", result.Deduplicated)} @@\n");
        if (result.Moved.Count == 0) return builder.ToString();
        builder.Append(targetExisted ? $"--- a/{target}\n" : "--- /dev/null\n").Append($"+++ b/{target}\n@@ members added (partial class part) @@\n");
        foreach (var signature in result.Signatures) builder.Append($"+{signature}\n");
        return builder.ToString();
    }
}

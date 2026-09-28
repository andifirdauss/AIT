using System.Text;

namespace AiThread.Service.Workspace;

// Compact unified-style diff built from the edit blocks themselves (unchanged leading/trailing lines become context).
public static class DiffBuilder
{
    private const int ContextLines = 3;

    public static string Build(string relativePath, IReadOnlyList<EditBlock> blocks, bool createsFile, int maxLines = 400)
    {
        var lines = new List<string>
        {
            createsFile ? "--- /dev/null" : $"--- a/{relativePath}",
            $"+++ b/{relativePath}"
        };

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var oldLines = block.IsWholeFile ? [] : Split(block.Search);
            var newLines = Split(block.Replace);

            var prefix = 0;
            while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;

            var suffix = 0;
            while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
                   && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;

            lines.Add($"@@ block {i + 1} @@");
            lines.AddRange(oldLines.Skip(Math.Max(0, prefix - ContextLines)).Take(Math.Min(prefix, ContextLines)).Select(l => " " + l));
            lines.AddRange(oldLines.Skip(prefix).Take(oldLines.Length - prefix - suffix).Select(l => "-" + l));
            lines.AddRange(newLines.Skip(prefix).Take(newLines.Length - prefix - suffix).Select(l => "+" + l));
            lines.AddRange(oldLines.Skip(oldLines.Length - suffix).Take(ContextLines).Select(l => " " + l));
        }

        if (lines.Count > maxLines)
        {
            var omitted = lines.Count - maxLines;
            lines = lines.Take(maxLines).ToList();
            lines.Add($"... ({omitted} more diff lines)");
        }

        var builder = new StringBuilder();
        foreach (var line in lines) builder.Append(line).Append('\n');
        return builder.ToString();
    }

    private static string[] Split(string text)
    {
        var normalized = TextFileIo.NormalizeNewLines(text);
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }
}

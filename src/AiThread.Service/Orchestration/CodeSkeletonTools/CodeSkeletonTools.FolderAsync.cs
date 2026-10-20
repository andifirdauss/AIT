using System.Text;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // One line per outlined file under the folder: its types with their member counts and its functions.
    private async Task<string> FolderAsync(ProjectContext project, WorkspaceScope scope, string full, CancellationToken cancellationToken)
    {
        if (!scope.IsInside(full)) return "Error: folder outlines cover the project only; use list_directory outside it.";
        var snapshot = await structure.GetSnapshotAsync(project.ProjectId, cancellationToken);
        var relative = scope.ToRelative(full);
        var prefix = relative == "." ? string.Empty : relative + "/";
        var files = snapshot.Files.Values
            .Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) return $"No files under '{(prefix.Length == 0 ? "." : relative)}' in the project outline.";

        var outlined = files.Where(f => f.Outline is { } o && (o.Types.Count > 0 || o.Functions.Count > 0)).ToList();
        var thirdParty = files.Count(f => f.ThirdParty);
        var builder = new StringBuilder();
        builder.Append($"{(prefix.Length == 0 ? "Project" : relative)}: {files.Count} files, {outlined.Count} with declarations");
        builder.Append(thirdParty > 0 ? $", {thirdParty} third-party (not outlined)" : string.Empty);
        builder.Append(". Member counts in (); call code_outline with a file path for line ranges and signatures.\n");
        var shown = 0;
        foreach (var file in outlined)
        {
            if (shown == MaxFolderFiles || builder.Length > MaxOutputChars) break;
            var outline = file.Outline!;
            var parts = outline.Types.Select(t => $"{t.Kind} {(t.Container is null ? t.Name : $"{t.Container}.{t.Name}")} ({t.Members.Count})").ToList();
            if (outline.Functions.Count > 0)
                parts.Add("functions " + string.Join(", ", outline.Functions.Take(MaxFunctionsListed).Select(f => f.Name))
                          + (outline.Functions.Count > MaxFunctionsListed ? $" +{outline.Functions.Count - MaxFunctionsListed}" : string.Empty));
            builder.Append($"- {file.Path}: {string.Join("; ", parts)}\n");
            shown++;
        }
        if (shown < outlined.Count) builder.Append($"... {outlined.Count - shown} more files with declarations: outline a subfolder.\n");
        if (snapshot.Truncated) builder.Append($"(The project has more than {CodeStructureScanner.MaxFiles} files; the outline covers the first ones.)\n");
        return builder.ToString();
    }
}

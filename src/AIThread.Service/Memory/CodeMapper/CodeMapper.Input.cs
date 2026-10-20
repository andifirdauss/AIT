using System.Text;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    private static string Input(ProjectContext project, IReadOnlyList<ProjectModule> modules, IEnumerable<FileGroup> groups)
    {
        var builder = new StringBuilder();
        builder.Append("# Project\n").Append(project.Name).Append("\n\n# Modules\n");
        if (modules.Count == 0) builder.Append("(none yet)\n");
        foreach (var module in modules)
        {
            builder.Append("- ").Append(module.Name);
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append(" (aliases: ").Append(module.Aliases.Replace(",", ", ")).Append(')');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append(": ").Append(module.Description);
            builder.Append('\n');
        }
        builder.Append("\n# Groups\n");
        foreach (var group in groups)
        {
            builder.Append("## ").Append(group.Id).Append(" stem ").Append(group.Stem).Append('\n');
            foreach (var path in group.Paths)
                builder.Append(path).Append(" [").Append(CodeMapHeuristics.Layer(path)?.ToString() ?? "?").Append("]\n");
        }
        return builder.ToString();
    }
}

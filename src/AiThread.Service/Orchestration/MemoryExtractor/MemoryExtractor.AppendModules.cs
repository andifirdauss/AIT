using System.Text;
using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    private static void AppendModules(StringBuilder builder, IReadOnlyList<ProjectModule> modules)
    {
        if (modules.Count == 0) builder.Append("(none yet)\n");
        foreach (var module in modules.Take(200))
        {
            builder.Append("- ").Append(module.Name);
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append(" (aliases: ").Append(module.Aliases.Replace(",", ", ")).Append(')');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append(": ").Append(module.Description);
            builder.Append('\n');
        }
    }
}

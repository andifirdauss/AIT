using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // "## User — current state" and "## User — change 2026-10-25 (thread 1a2b3c4d)" headings, each followed by its text.
    private static string FormatModuleMemory(IReadOnlyList<MemoryItemDto> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.Append("## ").Append(item.Module).Append(" — ");
            if (item.Kind == MemoryKind.Summary) builder.Append($"current state (id {item.Id})");
            else
            {
                builder.Append($"change {item.CreatedAt:yyyy-MM-dd}");
                if (item.SourceThreadId is { } thread) builder.Append($" (thread {ThreadAnchor.ShortId(thread)})");
            }
            builder.Append('\n').Append(item.Description.Trim()).Append("\n\n");
        }
        return builder.ToString();
    }
}

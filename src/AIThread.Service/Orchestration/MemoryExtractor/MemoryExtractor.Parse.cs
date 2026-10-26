using System.Text.Json.Nodes;
using AIThread.Service.Memory;
using AIThread.Service.Skills;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    // Null for {"entry":null} or an answer without a usable entry.
    private static ExtractedChange? Parse(string content)
    {
        if (SkillAi.ParseObject(content)?["entry"] is not JsonObject item || ChangeLog.ParseEntry(item) is not { } entry) return null;
        return new ExtractedChange(entry, SkillAi.Text(item["module"]), MemoryAi.Strings(item["other_modules"]), MemoryAi.Strings(item["keywords"]));
    }
}

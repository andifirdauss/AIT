using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.Skills;

public sealed partial class SkillAi
{
    public static JsonNode? ParseObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Parses the arguments of a tool call; null with an error outcome when they are not a JSON object.
    public static JsonObject? ParseArguments(string arguments, out PlannerToolOutcome? error)
    {
        error = null;
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            error = new PlannerToolOutcome($"Error: arguments are not valid JSON ({ex.Message}). Call the tool again with a JSON object.", null, true);
            return null;
        }
    }
}

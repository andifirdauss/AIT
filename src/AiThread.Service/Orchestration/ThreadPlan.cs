using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AiThread.Service.Orchestration;

// The planner's output, delivered through the submit_plan tool.
public sealed class ThreadPlan
{
    public string Summary { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public List<ContractItem> Contract { get; set; } = [];
    public List<PlanTask> Tasks { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // Accepts the plan either at the root or wrapped as {"plan": {...}}.
    public static bool TryParse(string json, out ThreadPlan? plan, out string? error)
    {
        plan = null;
        error = null;
        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is JsonObject obj && obj["plan"] is JsonObject inner && obj["tasks"] is null) node = inner;
            if (node is not JsonObject) throw new JsonException("The plan must be a JSON object.");

            plan = node.Deserialize<ThreadPlan>(JsonOptions);
            if (plan is null) throw new JsonException("Empty plan.");
            plan.Contract ??= [];
            plan.Tasks ??= [];
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // For models that ignore tool calling and print the plan as text.
    public static bool TryParseFromText(string text, out ThreadPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        if (!TryParse(text[start..(end + 1)], out var parsed, out _) || parsed is null) return false;
        if (parsed.Tasks.Count == 0 && string.IsNullOrWhiteSpace(parsed.Answer)) return false;
        plan = parsed;
        return true;
    }
}

public sealed class ContractItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class PlanTask
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Action { get; set; } = "edit";
    public string Instruction { get; set; } = string.Empty;
    public List<LineRange>? Focus { get; set; }
    public List<ContextRef>? Context { get; set; }
    public List<string>? DependsOn { get; set; }

    [JsonIgnore]
    public bool IsCreate => string.Equals(Action, "create", StringComparison.OrdinalIgnoreCase);
}

public sealed class LineRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class ContextRef
{
    public string File { get; set; } = string.Empty;
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Note { get; set; }
}

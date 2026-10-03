using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Skills;

// The model the skill orchestrator talks to: the one chosen in Setting → Skill, else the preferred active planner.
public sealed class SkillAi(INineRouterService router, ISettingService settingService, INodeService nodeService)
{
    public async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.SkillOrchestratorModel)) return settings.SkillOrchestratorModel;
        return (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()?.Model
               ?? throw new AppValidationException("Pilih model Skill Orchestrator di Setting → Skill (atau tambahkan node Planner aktif).");
    }

    // One JSON-only call; Json is null when the answer holds no readable JSON object.
    public async Task<(JsonNode? Json, string Model, int Tokens)> AskJsonAsync(string systemPrompt, string input, int maxTokens, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(cancellationToken);
        var result = await router.ChatAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages = [ChatMessages.System(systemPrompt), ChatMessages.User(input)],
            Temperature = 0.1,
            MaxTokens = maxTokens
        }, cancellationToken: cancellationToken);
        return (ParseObject(result.Content), model, result.TotalTokens);
    }

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

    public static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    public static int? Int(JsonNode? node) =>
        node is JsonValue value ? value.TryGetValue<int>(out var i) ? i : value.TryGetValue<double>(out var d) ? (int)d : null : null;

    public static double? Number(JsonNode? node) =>
        node is JsonValue value ? value.TryGetValue<double>(out var d) ? d : value.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null : null;

    public static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
}

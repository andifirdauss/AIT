using System.Text.Json.Nodes;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // The model's answer per group id; empty when the call failed (the groups then fall back to their name stem).
    private async Task<Dictionary<string, JsonObject>> AskAsync(ProjectContext project, FileGroup[] batch, CancellationToken cancellationToken)
    {
        var modules = await registry.ListAsync(project.Key, cancellationToken: cancellationToken);
        JsonNode? json = null;
        try
        {
            (json, _) = await ai.AskJsonAsync(AgentPrompts.CodeMapper, Input(project, modules, batch), MaxTokens, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map model call failed; using name stems for {Count} group(s).", batch.Length);
        }

        return (json?["groups"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(o => SkillAi.Text(o["id"]) is not null)
            .GroupBy(o => SkillAi.Text(o["id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }
}

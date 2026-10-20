using System.Text.Json.Nodes;
using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static List<RouterModelDto> ParseModels(string json)
    {
        var models = new List<RouterModelDto>();
        if (Property(JsonNode.Parse(json), "data") is not JsonArray data) return models;

        foreach (var item in data)
        {
            var id = GetString(item?["id"]);
            if (string.IsNullOrWhiteSpace(id)) continue;

            // Search/fetch pseudo-models are not chat models.
            var kind = GetString(item?["kind"]);
            if (kind is "webSearch" or "webFetch") continue;

            var ownedBy = GetString(item?["owned_by"]) ?? string.Empty;
            models.Add(new RouterModelDto(
                id,
                ownedBy,
                string.Equals(ownedBy, "combo", StringComparison.OrdinalIgnoreCase),
                GetInt(item?["context_length"]),
                GetInt(item?["max_completion_tokens"])));
        }

        return models
            .OrderByDescending(m => m.IsCombo)
            .ThenBy(m => m.OwnedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

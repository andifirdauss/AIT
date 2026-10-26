using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static ChatCompletionResult ParseCompletion(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new NineRouterException(Lang.T("Respons 9router kosong.", "Empty 9router response."));
        if (root["error"] is { } error)
            throw new NineRouterException(GetString(error["message"]) ?? error.ToJsonString(), 502);

        var choice = root["choices"]?[0];
        var message = choice?["message"];
        var toolCalls = new List<ToolCall>();
        if (message?["tool_calls"] is JsonArray calls)
        {
            var i = 0;
            foreach (var call in calls)
            {
                var name = GetString(call?["function"]?["name"]);
                if (string.IsNullOrEmpty(name)) continue;
                toolCalls.Add(new ToolCall(
                    GetString(call?["id"]) ?? $"call_{i}",
                    name,
                    GetString(call?["function"]?["arguments"]) ?? "{}"));
                i++;
            }
        }

        return new ChatCompletionResult(
            GetString(message?["content"]) ?? string.Empty,
            toolCalls,
            GetString(choice?["finish_reason"]),
            GetInt(root["usage"]?["prompt_tokens"]) ?? 0,
            GetInt(root["usage"]?["completion_tokens"]) ?? 0,
            CachedTokensOf(root["usage"]) ?? 0);
    }
}

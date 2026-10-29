using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static async Task<ChatCompletionResult> ReadStreamAsync(
        HttpResponseMessage response,
        Func<string, Task>? onDelta,
        Action onLine,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();
        var toolCalls = new SortedDictionary<int, ToolCallBuilder>();
        string? finishReason = null;
        int promptTokens = 0, completionTokens = 0, cachedTokens = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            onLine();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            JsonNode? chunk;
            try
            {
                chunk = JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }
            if (chunk is null) continue;

            if (chunk["error"] is { } error)
                throw new NineRouterException(GetString(error["message"]) ?? error.ToJsonString(), GetInt(error["code"]) ?? 502);

            if (chunk["usage"] is JsonObject usage)
            {
                promptTokens = GetInt(usage["prompt_tokens"]) ?? promptTokens;
                completionTokens = GetInt(usage["completion_tokens"]) ?? completionTokens;
                cachedTokens = CachedTokensOf(usage) ?? cachedTokens;
            }

            if (chunk["choices"] is not JsonArray { Count: > 0 } choices) continue;
            var choice = choices[0];
            var delta = choice?["delta"] ?? choice?["message"];

            if (GetString(delta?["content"]) is { Length: > 0 } text)
            {
                content.Append(text);
                if (onDelta is not null) await onDelta(text);
            }

            if (delta?["tool_calls"] is JsonArray callDeltas)
            {
                foreach (var callDelta in callDeltas)
                {
                    if (callDelta is null) continue;
                    var id = GetString(callDelta["id"]);
                    var index = ResolveToolCallIndex(toolCalls, GetInt(callDelta["index"]), id);
                    if (!toolCalls.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCalls[index] = builder;
                    }

                    if (!string.IsNullOrEmpty(id)) builder.Id = id;
                    if (GetString(callDelta["function"]?["name"]) is { Length: > 0 } name) builder.Name.Append(name);
                    if (GetString(callDelta["function"]?["arguments"]) is { Length: > 0 } arguments) builder.Arguments.Append(arguments);
                }
            }

            if (GetString(choice?["finish_reason"]) is { Length: > 0 } finish) finishReason = finish;
        }

        return new ChatCompletionResult(
            content.ToString(),
            toolCalls.Values.Select((b, i) => b.Build(i)).Where(c => c.Name.Length > 0).ToList(),
            finishReason,
            promptTokens,
            completionTokens,
            cachedTokens);
    }
}

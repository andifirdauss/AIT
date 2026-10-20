using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // OpenAI reports cache hits as usage.prompt_tokens_details.cached_tokens; 9router's Claude translation adds
    // cache_read_input_tokens. Both are already counted in prompt_tokens.
    public static int? CachedTokensOf(JsonNode? usage) =>
        GetInt(usage?["prompt_tokens_details"]?["cached_tokens"])
        ?? GetInt(usage?["cache_read_input_tokens"])
        ?? GetInt(usage?["cached_tokens"]);
}

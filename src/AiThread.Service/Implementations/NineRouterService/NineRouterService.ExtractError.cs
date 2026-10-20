using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        try
        {
            // The body may be any JSON shape ({"error":"..."}, an array, a bare string), so never index a non-object.
            var node = JsonNode.Parse(body);
            var error = Property(node, "error");
            var message = GetString(Property(error, "message")) ?? GetString(Property(node, "message")) ?? GetString(error) ?? GetString(node);
            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw text.
        }
        return body.Length > 500 ? body[..500] : body;
    }
}

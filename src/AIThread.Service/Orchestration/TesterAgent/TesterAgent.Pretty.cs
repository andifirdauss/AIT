using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // JSON indented, so the developer can read it; anything else as it came.
    private static string Pretty(string body)
    {
        if (!LooksLikeJson(body)) return body;
        try
        {
            return JsonNode.Parse(body)?.ToJsonString(PrettyJson) ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}

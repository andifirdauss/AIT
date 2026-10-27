using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.Workspace.Web;

public sealed partial class WebFetcher
{
    // The body as text for the model: HTML as Markdown, JSON indented, other text as it is; binary bodies are refused.
    private static (string Text, string? Title, string? Error) Convert(byte[] body, MediaTypeHeaderValue? contentType, Uri url)
    {
        var media = contentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
        var textual = media.Contains("json") || media.Contains("xml") || media.Contains("javascript");
        if (!textual && BinaryTypes.Any(media.StartsWith))
            return (string.Empty, null, $"the response is binary ({media}, {body.Length} bytes); web_fetch only reads text.");
        if (body.Length == 0) return (string.Empty, null, null);

        var text = Decode(body, contentType?.CharSet);
        if (text.AsSpan(0, Math.Min(text.Length, 4096)).Contains('\0'))
            return (string.Empty, null, $"the response is binary ({(media.Length == 0 ? "no content type" : media)}, {body.Length} bytes); web_fetch only reads text.");

        var trimmed = text.TrimStart();
        if (media.Contains("html") || (media.Length == 0 && trimmed.StartsWith('<') && trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)))
            return (HtmlText.ToMarkdown(text, url, out var title), title, null);

        if (media.Contains("json") || (!media.Contains("xml") && trimmed.Length > 0 && trimmed[0] is '{' or '['))
        {
            try
            {
                if (JsonNode.Parse(text) is { } json) return (json.ToJsonString(Indented), null, null);
            }
            catch (JsonException)
            {
                // Not JSON after all (or cut off): the text as it came.
            }
        }
        return (text, null, null);
    }
}

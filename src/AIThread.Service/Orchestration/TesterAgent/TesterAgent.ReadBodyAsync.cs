using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Text bodies up to MaxReadBytes; binary ones only by their size.
    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxReadBytes];
        var read = 0;
        int count;
        while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0) read += count;
        var truncated = read == buffer.Length && await stream.ReadAsync(new byte[1], cancellationToken) > 0;

        var isText = mediaType.Length == 0 || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                     || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                     || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("form", StringComparison.OrdinalIgnoreCase);
        if (!isText) return $"(binary {mediaType}, {read}{(truncated ? "+" : string.Empty)} bytes)";

        Encoding encoding;
        try
        {
            encoding = response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset ? Encoding.GetEncoding(charset.Trim('"')) : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }
        var text = encoding.GetString(buffer, 0, read);
        return truncated ? text + "\n… (body truncated)" : text;
    }
}

using System.Text;
using System.Text.Json;

namespace AiThread.Service.Workspace.Documents;

public sealed class IpynbExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".ipynb" };

    public async Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;

        if (!root.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
            return "";

        var builder = new StringBuilder();
        var n = 0;
        foreach (var cell in cells.EnumerateArray())
        {
            n++;
            var cellType = cell.TryGetProperty("cell_type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? "code"
                : "code";

            builder.Append("--- Cell ").Append(n).Append(" [").Append(cellType).Append("] ---\n");

            if (cell.TryGetProperty("source", out var source))
                AppendPiece(builder, JoinText(source));

            if (cell.TryGetProperty("outputs", out var outputs) &&
                outputs.ValueKind == JsonValueKind.Array &&
                outputs.GetArrayLength() > 0)
            {
                builder.Append("Output:\n");
                foreach (var output in outputs.EnumerateArray())
                    AppendPiece(builder, OutputText(output));
            }

            builder.Append('\n');
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    static void AppendPiece(StringBuilder builder, string piece)
    {
        var trimmed = piece.TrimEnd('\r', '\n');
        if (trimmed.Length == 0)
            return;
        builder.Append(trimmed).Append('\n');
    }

    static string OutputText(JsonElement output)
    {
        var outputType = output.TryGetProperty("output_type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        switch (outputType)
        {
            case "stream":
                return output.TryGetProperty("text", out var text) ? JoinText(text) : "";

            case "execute_result":
            case "display_data":
                if (!output.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return "";

                if (data.TryGetProperty("text/plain", out var plain))
                    return JoinText(plain);

                foreach (var property in data.EnumerateObject())
                {
                    if (property.Name.StartsWith("image/", StringComparison.Ordinal))
                        return "[image output omitted]";
                }
                return "";

            case "error":
                var ename = output.TryGetProperty("ename", out var enameElement) ? JoinText(enameElement) : "";
                var evalue = output.TryGetProperty("evalue", out var evalueElement) ? JoinText(evalueElement) : "";
                return $"{ename}: {evalue}";

            default:
                return "";
        }
    }

    static string JoinText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? "";

        if (element.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    sb.Append(item.GetString());
            }
            return sb.ToString();
        }

        return "";
    }
}

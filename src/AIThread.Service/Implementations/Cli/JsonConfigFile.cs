using System.Text.Json.Nodes;
using System.Text.Json;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

// JSON config files of CLIs that also accept comments (opencode, Qwen Code). Reading tolerates comments; writing
// refuses a file that has them, because System.Text.Json cannot write them back.
public static class JsonConfigFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonDocumentOptions NoComments = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = true };

    public static JsonObject Read(string content, string fileName) => Parse(content, fileName, Lenient);

    // For a file about to be rewritten.
    public static JsonObject ReadForWrite(string content, string fileName)
    {
        var root = Read(content, fileName);
        try
        {
            Parse(content, fileName, NoComments);
        }
        catch (AppValidationException)
        {
            throw new AppValidationException(Lang.T($"{fileName} berisi komentar. AIT tidak bisa menulisnya tanpa menghapus komentar Anda; hapus komentarnya dulu atau tambahkan entri secara manual.", $"{fileName} contains comments. AIT cannot write it without removing your comments; remove the comments first or add the entry manually."));
        }
        return root;
    }

    public static string Write(JsonObject root) => root.ToJsonString(WriteOptions) + Environment.NewLine;

    // The node at a path of object keys, or null when any step is missing or not an object.
    public static JsonNode? At(JsonNode? node, params string[] path)
    {
        foreach (var key in path) node = (node as JsonObject)?[key];
        return node;
    }

    public static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Parse(string content, string fileName, JsonDocumentOptions options)
    {
        if (string.IsNullOrWhiteSpace(content)) return new JsonObject();
        try
        {
            return JsonNode.Parse(content, documentOptions: options) as JsonObject
                   ?? throw new AppValidationException(Lang.T($"{fileName} bukan objek JSON.", $"{fileName} is not a JSON object."));
        }
        catch (JsonException ex)
        {
            throw new AppValidationException(Lang.T($"{fileName} tidak dapat dibaca sebagai JSON: {ex.Message}", $"{fileName} cannot be read as JSON: {ex.Message}"));
        }
    }
}

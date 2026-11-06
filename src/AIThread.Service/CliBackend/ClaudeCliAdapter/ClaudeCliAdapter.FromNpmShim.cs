using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // <npm prefix>\claude.cmd → <npm prefix>\node_modules\@anthropic-ai\claude-code, entry from package.json "bin".
    private static Launch? FromNpmShim(string shim)
    {
        var folder = Path.GetDirectoryName(shim);
        if (folder is null) return null;
        var package = Path.Combine(folder, "node_modules", "@anthropic-ai", "claude-code");
        var manifest = Path.Combine(package, "package.json");
        if (!File.Exists(manifest)) return null;

        string? entry;
        try
        {
            var bin = JsonNode.Parse(File.ReadAllText(manifest))?["bin"];
            entry = bin is JsonObject map ? map["claude"]?.GetValue<string>() : bin?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(entry)) return null;

        var target = Path.GetFullPath(Path.Combine(package, entry));
        if (!File.Exists(target)) return null;
        if (!target.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && !target.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
            return new Launch(target, []);

        var node = Existing(Path.Combine(folder, "node.exe")) ?? FindOnPath("node.exe");
        return node is null ? null : new Launch(node, [target]);
    }
}

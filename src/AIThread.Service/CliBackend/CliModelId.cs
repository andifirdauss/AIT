using System.Diagnostics.CodeAnalysis;

namespace AIThread.Service.CliBackend;

// A node model served by an official coding CLI on this machine instead of 9router: "cli:<tool>:<model>", e.g.
// "cli:claude:opus", "cli:codex:gpt-5.5", "cli:gemini:pro". The CLI runs with its own login (whoever signed it in), so a developer without a 9router account
// can still work through AIT. An empty model ("cli:claude") means the CLI's own default model.
public sealed record CliModelId(string Tool, string Model)
{
    public const string Prefix = "cli:";
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Gemini = "gemini";
    public const string Antigravity = "agy";

    public string Id => Model.Length == 0 ? $"{Prefix}{Tool}" : $"{Prefix}{Tool}:{Model}";

    public static bool IsCli([NotNullWhen(true)] string? model) =>
        model is not null && model.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    // The model part keeps everything after the tool, colons included (full model names never need them today, but a
    // CLI might).
    public static bool TryParse(string? model, [NotNullWhen(true)] out CliModelId? id)
    {
        id = null;
        if (!IsCli(model)) return false;
        var rest = model[Prefix.Length..].Trim();
        var colon = rest.IndexOf(':');
        var tool = (colon < 0 ? rest : rest[..colon]).Trim().ToLowerInvariant();
        if (tool.Length == 0) return false;
        id = new CliModelId(tool, colon < 0 ? string.Empty : rest[(colon + 1)..].Trim());
        return true;
    }

    public static string Format(string tool, string model) => new CliModelId(tool, model).Id;
}

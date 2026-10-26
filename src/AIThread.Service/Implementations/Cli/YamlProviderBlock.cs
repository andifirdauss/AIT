using System.Text.RegularExpressions;
using System.Text;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations.Cli;

// Owns one provider entry in oh-my-pi's models.yml, between two comment markers, leaving every other line as the
// user wrote it. When the file has no top-level "providers:" key the block carries that key itself, so removing the
// block never leaves an empty "providers:" behind (omp rejects the whole file then). Only block-style YAML is
// handled: a flow-style "providers: {...}" or the user's own entry with the same provider id is refused.
public static partial class YamlProviderBlock
{
    public const string Begin = ManagedTextBlock.Begin;
    public const string End = ManagedTextBlock.End;

    // providerLines: the provider entry relative to its own indentation, e.g. ["aithread:", "  baseUrl: \"...\""].
    public static string Apply(string? content, string providerId, IReadOnlyList<string> providerLines)
    {
        var text = Remove(content ?? string.Empty);
        var newLine = NewLine(content);
        var lines = Lines(text);

        if (Conflict(text, providerId) is { } conflict) throw new InvalidOperationException(conflict);

        var providers = lines.FindIndex(l => ProvidersKey().IsMatch(l));
        if (providers < 0)
        {
            var block = new List<string> { Begin, "providers:" };
            block.AddRange(providerLines.Select(l => "  " + l));
            block.Add(End);
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(string.Empty);
            lines.AddRange(block);
        }
        else
        {
            var indent = ChildIndent(lines, providers) ?? "  ";
            var block = new List<string> { Begin };
            block.AddRange(providerLines.Select(l => indent + l));
            block.Add(End);
            lines.InsertRange(providers + 1, block);
        }

        return string.Join(newLine, lines) + newLine;
    }

    public static string Remove(string content) => ManagedTextBlock.Remove(content);

    // A problem that makes writing the block unsafe, phrased for the user; null when it can be written.
    public static string? Conflict(string content, string providerId)
    {
        var lines = Lines(Remove(content));
        if (lines.Any(l => FlowProviders().IsMatch(l)))
            return Lang.T("models.yml memakai bentuk \"providers: { ... }\" dalam satu baris. Ubah ke bentuk blok (satu provider per baris) dulu.", "models.yml uses the one-line form \"providers: { ... }\". Change it to the block form (one provider per line) first.");

        var providers = lines.FindIndex(l => ProvidersKey().IsMatch(l));
        if (providers < 0) return null;

        var indent = ChildIndent(lines, providers);
        for (var i = providers + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0 || lines[i].TrimStart().StartsWith('#')) continue;
            if (!char.IsWhiteSpace(lines[i][0])) break;
            if (indent is not null && lines[i].StartsWith(indent) && !char.IsWhiteSpace(lines[i][indent.Length])
                && lines[i].Trim().StartsWith(providerId + ":", StringComparison.Ordinal))
                return Lang.T($"models.yml sudah punya provider \"{providerId}\" buatan Anda. Hapus atau ganti namanya dulu.", $"models.yml already has a provider \"{providerId}\" made by you. Delete or rename it first.");
        }
        return null;
    }

    // The lines inside the managed block, or null when there is none.
    public static IReadOnlyList<string>? Managed(string content) => ManagedTextBlock.Managed(content);

    // Double-quoted YAML scalar; valid for any value. Also a valid TOML basic string (same escapes).
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        }
        return builder.Append('"').ToString();
    }

    // Indentation of the first entry under "providers:", or null when it has none yet.
    private static string? ChildIndent(List<string> lines, int providers)
    {
        for (var i = providers + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            if (!char.IsWhiteSpace(line[0])) return null;
            return line[..(line.Length - line.TrimStart().Length)];
        }
        return null;
    }

    private static List<string> Lines(string content) => ManagedTextBlock.Lines(content);

    private static string NewLine(string? content) => ManagedTextBlock.NewLine(content);

    [GeneratedRegex(@"^providers:\s*(#.*)?$")]
    private static partial Regex ProvidersKey();

    [GeneratedRegex(@"^providers:\s*[\{\[]")]
    private static partial Regex FlowProviders();
}

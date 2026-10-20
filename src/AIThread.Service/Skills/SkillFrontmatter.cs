namespace AIThread.Service.Skills;

public sealed record SkillDocument(IReadOnlyDictionary<string, string> Fields, string Body);

// The YAML frontmatter of a SKILL.md is only read for flat keys (name, description, ...), including folded (>) and
// literal (|) blocks; nested values are ignored. Anything that is not frontmatter stays in the body.
public static class SkillFrontmatter
{
    public static SkillDocument Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var content = (text ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n");
        if (!content.StartsWith("---\n", StringComparison.Ordinal)) return new SkillDocument(fields, content.Trim());

        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return new SkillDocument(fields, content.Trim());

        var lines = content[4..end].Split('\n');
        var bodyStart = content.IndexOf('\n', end + 4);
        var body = bodyStart < 0 ? string.Empty : content[(bodyStart + 1)..];

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value is ">" or "|" or ">-" or "|-" or ">+" or "|+")
            {
                var block = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                    block.Add(lines[++i].Trim());
                value = value.StartsWith('>')
                    ? string.Join(' ', block.Where(b => b.Length > 0))
                    : string.Join('\n', block).Trim();
            }
            else
            {
                value = Unquote(value);
            }

            if (value.Length > 0) fields[key] = value;
        }

        return new SkillDocument(fields, body.Trim());
    }

    // First prose paragraph of a Markdown body, for skills without a description.
    public static string FirstParagraph(string body, int maxChars = 300)
    {
        var paragraph = string.Join(' ', body.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .SkipWhile(l => l.Length == 0 || l.StartsWith('#') || l.StartsWith("```", StringComparison.Ordinal))
            .TakeWhile(l => l.Length > 0 && !l.StartsWith('#')));
        return paragraph.Length <= maxChars ? paragraph : paragraph[..maxChars].TrimEnd() + "…";
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
            ? value[1..^1].Replace("\\\"", "\"").Replace("''", "'")
            : value;
}

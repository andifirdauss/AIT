using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Skills;

// RelativeDir uses forward slashes ("." = the source folder); Body is the entry file without its frontmatter.
public sealed record DiscoveredSkill(string RelativeDir, string EntryFile, string Name, string Description, string Body)
{
    public string? Tags { get; init; }
}

// Finds skills in a pulled source folder without a model: every SKILL.md (Claude Code / Agent Skills layout).
public static class SkillDiscovery
{
    public const string EntryFileName = "SKILL.md";
    public const int MaxContentChars = 200_000;

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "__pycache__", ".venv", "venv", "dist", "build", ".idea", ".vs"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".svg", ".pdf", ".zip", ".gz", ".tar", ".7z", ".exe", ".dll",
        ".woff", ".woff2", ".ttf", ".otf", ".mp3", ".mp4", ".mov", ".wav", ".pyc", ".bin", ".lock"
    };

    // rootName names a SKILL.md found at the root itself (e.g. a single downloaded file).
    public static IReadOnlyList<DiscoveredSkill> Scan(string root, string rootName)
    {
        var skills = new List<DiscoveredSkill>();
        foreach (var file in EnumerateFiles(root).Where(f => Path.GetFileName(f).Equals(EntryFileName, StringComparison.OrdinalIgnoreCase)))
        {
            var relativeDir = Relative(root, Path.GetDirectoryName(file)!);
            var folderName = relativeDir == "." ? rootName : relativeDir.Split('/')[^1];
            skills.Add(Read(root, file, relativeDir, folderName, null, null));
        }
        return skills.OrderBy(s => s.RelativeDir, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A Markdown file the skill orchestrator identified as a skill in a repository without SKILL.md files.
    public static DiscoveredSkill? FromFile(string root, string relativePath, string? name, string? description)
    {
        var normalized = relativePath.Replace('\\', '/').Trim().TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;

        var relativeDir = Relative(root, Path.GetDirectoryName(full)!);
        return Read(root, full, relativeDir, Path.GetFileNameWithoutExtension(full), name, description);
    }

    // Files a model can look at to tell where the skills are: text files only, ignoring build and VCS folders.
    public static IReadOnlyList<string> FileTree(string root, int maxEntries) =>
        EnumerateFiles(root)
            .Where(f => !BinaryExtensions.Contains(Path.GetExtension(f)))
            .Select(f => Relative(root, f))
            .OrderBy(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(maxEntries)
            .ToList();

    public static string? ReadmeExcerpt(string root, int maxChars)
    {
        var readme = Directory.Exists(root)
            ? Directory.EnumerateFiles(root).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("readme", StringComparison.OrdinalIgnoreCase))
            : null;
        if (readme is null) return null;
        var text = File.ReadAllText(readme);
        return text.Length <= maxChars ? text : text[..maxChars] + "\n…";
    }

    // Files of a skill folder relative to it, for the detail view.
    public static IReadOnlyList<string> ListFiles(string folder, int max) =>
        Directory.Exists(folder) ? EnumerateFiles(folder).Select(f => Relative(folder, f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(max).ToList() : [];

    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    public static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    private static DiscoveredSkill Read(string root, string file, string relativeDir, string fallbackName, string? name, string? description)
    {
        var document = SkillFrontmatter.Parse(File.ReadAllText(file));
        var finalName = FirstText(name, document.Fields.GetValueOrDefault("name"), fallbackName)!;
        var body = Limit(document.Body, MaxContentChars);
        var finalDescription = FirstText(description, document.Fields.GetValueOrDefault("description"), SkillFrontmatter.FirstParagraph(body)) ?? string.Empty;
        return new DiscoveredSkill(relativeDir, Path.GetFileName(file), Limit(finalName.Trim(), 200), Limit(finalDescription.Trim(), 2000), body);
    }

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!IgnoredDirectories.Contains(Path.GetFileName(child))) pending.Push(child);
        }
    }

    private static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative is "" ? "." : relative;
    }
}

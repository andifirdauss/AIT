using System.Text.Json;
using AIThread.Service.Workspace;
using Microsoft.Extensions.FileSystemGlobbing;

namespace AIThread.Service.Security;

// A project's own secret rules, committed with it in .aithread/project.json:
//   "secrets": {
//     "deny":  ["config/production/**", "*.secret.json"],   more files never read for a model
//     "allow": ["tests/certs/test.pem"],                     credential files that are fine to read (test fixtures)
//     "keys":  ["ConnectionKey", "LicenseCode"]               more setting names whose values are masked
//   }
// Globs are relative to the project root, like .gitignore; a bare name matches at any depth.
public sealed class SecretPolicy
{
    public const string SectionName = "secrets";

    public static readonly SecretPolicy Empty = new([], [], []);

    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private readonly Matcher? _deny;
    private readonly Matcher? _allow;

    public SecretPolicy(IReadOnlyList<string> deny, IReadOnlyList<string> allow, IReadOnlyList<string> keys)
    {
        Deny = deny;
        Allow = allow;
        Keys = keys;
        _deny = CreateMatcher(deny);
        _allow = CreateMatcher(allow);
    }

    public IReadOnlyList<string> Deny { get; }
    public IReadOnlyList<string> Allow { get; }
    public IReadOnlyList<string> Keys { get; }

    // A missing or unreadable file means no project rules; the built-in ones still apply.
    public static SecretPolicy Load(string root)
    {
        var path = Path.Combine(root, ProjectKeyResolver.OverrideFile);
        try
        {
            if (!File.Exists(path)) return Empty;
            using var document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(SectionName, out var section)
                || section.ValueKind != JsonValueKind.Object)
                return Empty;
            return new SecretPolicy(Strings(section, "deny"), Strings(section, "allow"), Strings(section, "keys"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Empty;
        }
    }

    public bool Denies(string relativePath) => _deny?.Match(relativePath).HasMatches == true;

    public bool Allows(string relativePath) => _allow?.Match(relativePath).HasMatches == true;

    private static List<string> Strings(JsonElement section, string name) =>
        section.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                .Select(e => e.GetString()!.Trim())
                .ToList()
            : [];

    private static Matcher? CreateMatcher(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0) return null;
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            var normalized = pattern.Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
            matcher.AddInclude(normalized.Contains('/') ? normalized : "**/" + normalized);
        }
        return matcher;
    }
}

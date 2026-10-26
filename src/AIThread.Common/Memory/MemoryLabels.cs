using System.Text.RegularExpressions;
using AIThread.Common.Enums;

namespace AIThread.Common.Memory;

// The memory label: [Area ·] Module[+Modules] · Layer(s) · Kind (the first search layer) plus up to 16 keywords (the second).
// Everything is PascalCase with acronyms in capitals (API, DTO, UI); identifiers taken from code keep their own
// casing apart from the first letter, so "UserDto" stays "UserDto" while "user dto" becomes "UserDTO". Every write
// path normalizes here, never trusting a model to do it.
public static partial class MemoryLabels
{
    // Rows with a LabelVersion below this are migrated by MemoryRelabelWorker.
    public const int CurrentVersion = 1;

    public const int MaxKeywords = 16;
    public const int MaxKeywordLength = 60;
    public const int MaxModuleLength = 100;

    // Modules besides the main one that a single memory may concern.
    public const int MaxExtraModules = 3;
    public const int MaxAreaLength = 100;

    // Registry key of the modules of knowledge memory (technologies and domains, shared by every project).
    public const string KnowledgeKey = "knowledge";

    private static readonly object Gate = new();

    private static HashSet<string> _acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "AI", "API", "AWS", "BL", "CD", "CDN", "CI", "CLI", "CRM", "CRUD", "CSS", "CSV", "DAL", "DB", "DI", "DNS", "DTO",
        "EF", "ERP", "GCP", "GRPC", "GUID", "HR", "HTML", "HTTP", "HTTPS", "ID", "IIS", "IO", "IP", "JS", "JSON", "JWT",
        "KPI", "LLM", "MCP", "MVC", "ORM", "OS", "OTP", "PDF", "PO", "PWA", "QA", "REST", "RPC", "SDK", "SMTP", "SPA",
        "SQL", "SSE", "SSH", "SSO", "TCP", "TS", "UDP", "UI", "URI", "URL", "UUID", "UX", "VM", "XML", "YAML"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "when", "then", "than", "are", "was", "will",
        "yang", "dan", "untuk", "dengan", "ini", "itu", "dari", "pada", "akan", "juga", "tidak", "ada", "bisa",
        "tolong", "buat", "buatkan", "tambah", "tambahkan", "ubah", "please", "make", "add", "update", "change"
    };

    private static readonly HashSet<MemoryKind> KnowledgeKinds = [MemoryKind.Pitfall, MemoryKind.Pattern, MemoryKind.Architecture];

    // Acronyms added in appsettings (AIThread:MemoryAcronyms) on top of the built-in list.
    public static void AddAcronyms(IEnumerable<string>? acronyms)
    {
        if (acronyms is null) return;
        lock (Gate)
        {
            var next = new HashSet<string>(_acronyms, StringComparer.OrdinalIgnoreCase);
            foreach (var acronym in acronyms)
            {
                var trimmed = acronym?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.All(char.IsLetterOrDigit)) next.Add(trimmed);
            }
            _acronyms = next;
        }
    }

    public static bool IsAcronym(string word) => _acronyms.Contains(word);

    public static string Pascal(string? text) => Pascal(text, keepCapitals: false);

    // keepCapitals: a word written in capitals stays so whatever its length (module codes such as "MKPP").
    private static string Pascal(string? text, bool keepCapitals)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return string.Empty;
        if (IsIdentifier(trimmed)) return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];

        var parts = Separators().Split(trimmed)
            .Where(w => w.Length > 0)
            .SelectMany(w => CamelHumps().Split(w))
            .Where(w => w.Length > 0)
            .Select(w => keepCapitals && w.Length >= 2 && w.Any(char.IsLetter) && !w.Any(char.IsLower) ? w : Word(w));
        return string.Concat(parts);
    }

    // A single token with a camel hump (an uppercase letter right after a lowercase one or a digit): a name from code.
    public static bool IsIdentifier(string token)
    {
        if (token.Length < 2 || !char.IsLetter(token[0])) return false;
        var hump = false;
        for (var i = 0; i < token.Length; i++)
        {
            if (!char.IsLetterOrDigit(token[i])) return false;
            if (i > 0 && char.IsUpper(token[i]) && (char.IsLower(token[i - 1]) || char.IsDigit(token[i - 1]))) hump = true;
        }
        return hump;
    }

    // Module names are often abbreviations, so they keep their capitals: "MKPP" stays, "Klaim DF" becomes "KlaimDF",
    // "KlaimDF" stays. Only what is written in lower case gets the PascalCase capital ("klaimdf" → "Klaimdf").
    public static string? Module(string? text)
    {
        var name = Clip(Pascal(text, keepCapitals: true), MaxModuleLength);
        return name.Length == 0 ? null : name;
    }

    // The other modules of a memory: normalized, without the main module or a duplicate (same name written
    // differently), at most MaxExtraModules.
    public static IReadOnlyList<string> ExtraModules(IEnumerable<string?>? names, string? main)
    {
        var result = new List<string>();
        foreach (var raw in names ?? [])
        {
            if (Module(raw) is not { } name) continue;
            if ((main is not null && SameName(name, main)) || result.Any(r => SameName(r, name))) continue;
            result.Add(name);
            if (result.Count == MaxExtraModules) break;
        }
        return result;
    }

    // Main module first, then the extra ones.
    public static IReadOnlyList<string> AllModules(string? main, IEnumerable<string>? extra) =>
        main is null ? [] : [main, .. extra ?? []];

    // Whether a memory (main module + extra modules CSV) concerns the module, ignoring case.
    public static bool HasModule(string? main, string? extraCsv, string module) =>
        string.Equals(main, module, StringComparison.OrdinalIgnoreCase)
        || SplitCsv(extraCsv).Contains(module, StringComparer.OrdinalIgnoreCase);

    public static string? Area(string? text)
    {
        var name = Clip(Pascal(text), MaxAreaLength);
        return name.Length == 0 ? null : name;
    }

    // Normalized, without duplicates (ignoring case), without the label parts themselves, at most 16.
    public static IReadOnlyList<string> Keywords(IEnumerable<string?>? keywords, params string?[] exclude)
    {
        var excluded = new HashSet<string>(exclude.Where(e => !string.IsNullOrWhiteSpace(e))!, StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<MemoryLayer>().Concat(Enum.GetNames<MemoryKind>())) excluded.Add(name);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in keywords ?? [])
        {
            var keyword = Clip(Pascal(raw), MaxKeywordLength);
            if (keyword.Length < 2 || excluded.Contains(keyword) || !seen.Add(keyword)) continue;
            result.Add(keyword);
            if (result.Count == MaxKeywords) break;
        }
        return result;
    }

    public static IReadOnlyList<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? JoinCsv(IEnumerable<string>? values)
    {
        var list = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];
        return list.Count == 0 ? null : string.Join(",", list);
    }

    public static IReadOnlyList<MemoryLayer> ParseLayers(string? csv) =>
        SplitCsv(csv).Select(s => TryParseLayer(s, out var layer) ? layer : (MemoryLayer?)null).OfType<MemoryLayer>().Distinct().Order().ToList();

    public static string? LayersCsv(IEnumerable<MemoryLayer>? layers) =>
        JoinCsv(layers?.Distinct().Order().Select(l => l.ToString()));

    // Layer names, plus the words a model or a developer tends to use for them.
    public static bool TryParseLayer(string? text, out MemoryLayer layer)
    {
        layer = default;
        var value = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value)) return false;
        MemoryLayer? parsed = value switch
        {
            "ui" or "view" or "views" or "frontend" or "page" or "component" or "presentation" => MemoryLayer.UI,
            "entry" or "controller" or "route" or "handler" or "endpoint" or "command" => MemoryLayer.Entry,
            "logic" or "service" or "business" or "domain" or "integration" or "bl" => MemoryLayer.Logic,
            "data" or "repository" or "persistence" or "database" or "db" or "model" or "contract" or "entity" or "dto" => MemoryLayer.Data,
            "infra" or "infrastructure" or "config" or "configuration" or "build" or "startup" => MemoryLayer.Infra,
            "test" or "tests" or "testing" => MemoryLayer.Test,
            _ => null
        };
        if (parsed is null) return false;
        layer = parsed.Value;
        return true;
    }

    public static bool TryParseKind(string? text, out MemoryKind kind)
    {
        kind = default;
        var value = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value)) return false;
        MemoryKind? parsed = value switch
        {
            "pitfall" or "bug" or "trap" or "gotcha" => MemoryKind.Pitfall,
            "rule" or "validation" or "businessrule" or "business rule" => MemoryKind.Rule,
            "flow" or "process" or "sequence" => MemoryKind.Flow,
            "convention" or "style" => MemoryKind.Convention,
            "architecture" or "design" or "structure" => MemoryKind.Architecture,
            "pattern" or "solution" or "technique" => MemoryKind.Pattern,
            "change" or "changelog" or "change log" or "perubahan" => MemoryKind.Change,
            "summary" or "state" or "ringkasan" => MemoryKind.Summary,
            _ => null
        };
        if (parsed is null) return false;
        kind = parsed.Value;
        return true;
    }

    // Knowledge memory is never project specific, so business rules, flows, conventions, change entries and module
    // summaries cannot be knowledge.
    public static bool KindAllowedFor(MemoryType type, MemoryKind kind) => type != MemoryType.Knowledge || KnowledgeKinds.Contains(kind);

    // A single fact as older versions wrote it (Pitfall..Pattern), not a change entry or a module summary.
    public static bool IsFact(MemoryKind? kind) => kind is >= MemoryKind.Pitfall and <= MemoryKind.Pattern;

    // "User · Logic/Entry · Rule", with the area in front for a repository that holds several apps; a memory of several
    // modules reads "Order+Invoice · Logic · Flow".
    public static string Display(string? area, string module, IEnumerable<MemoryLayer>? layers, MemoryKind kind, IEnumerable<string>? extraModules = null)
    {
        var layerText = string.Join("/", (layers ?? []).Distinct().Order());
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(area)) parts.Add(area);
        parts.Add(string.Join("+", AllModules(module, extraModules)));
        if (layerText.Length > 0) parts.Add(layerText);
        parts.Add(kind.ToString());
        return string.Join(" · ", parts);
    }

    // The old Label column, still filled so older AIT versions sharing the store can read new memories.
    public static string Compose(string display, IEnumerable<string> keywords)
    {
        var list = keywords.ToList();
        return Clip(list.Count == 0 ? display : $"{display} | {string.Join(", ", list)}", 300);
    }

    // Search tokens of free text (a prompt or a query): words in lower case, with identifiers also split at their
    // humps ("UserProfileService" → userprofileservice, user, profile, service). Common words are dropped.
    public static IReadOnlyList<string> Tokens(string? text, int max = 60)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Words().Matches(text))
        {
            foreach (var token in WordTokens(match.Value))
            {
                if (!Keep(token) || !seen.Add(token)) continue;
                result.Add(token);
                if (result.Count == max) return result;
            }
        }
        return result;
    }

    // What a keyword matches: the whole keyword and its humps, in lower case.
    public static IReadOnlyCollection<string> KeywordTokens(string keyword)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Words().Matches(keyword))
            foreach (var token in WordTokens(match.Value))
                if (token.Length >= 2) set.Add(token);
        return set;
    }

    // Exact token, or the same word with a plural "s"/"es".
    public static bool TokenMatches(string a, string b) =>
        a == b
        || (a.Length > 3 && b.Length > 3 && (a == b + "s" || b == a + "s" || a == b + "es" || b == a + "es"));

    // Names that are the same module written differently: case, separators, or an English plural.
    public static bool SameName(string a, string b)
    {
        var x = Compact(a);
        var y = Compact(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;
        return PluralOf(x, y) || PluralOf(y, x);
    }

    // Letters and digits only, lower case.
    public static string Compact(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool PluralOf(string plural, string singular) =>
        singular.Length >= 3
        && (plural == singular + "s" || plural == singular + "es"
            || (singular.EndsWith('y') && plural == singular[..^1] + "ies"));

    private static IEnumerable<string> WordTokens(string word)
    {
        yield return word.ToLowerInvariant();
        var humps = CamelHumps().Split(word).Where(h => h.Length > 0).ToList();
        if (humps.Count < 2) yield break;
        foreach (var hump in humps) yield return hump.ToLowerInvariant();
    }

    private static bool Keep(string token) =>
        !StopWords.Contains(token) && (token.Length >= 3 || (token.Length == 2 && IsAcronym(token)));

    // A short word written in capitals is kept as an acronym even when it is not in the list ("TO", "PO", "SKU");
    // a longer one only when listed ("USER" → "User", "JSON" stays).
    private static string Word(string part)
    {
        if (IsAcronym(part) || (part.Length is >= 2 and <= 3 && part.All(char.IsUpper))) return part.ToUpperInvariant();
        if (part.All(char.IsDigit)) return part;
        return char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"(?<=[\p{Ll}\p{N}])(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})")]
    private static partial Regex CamelHumps();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}

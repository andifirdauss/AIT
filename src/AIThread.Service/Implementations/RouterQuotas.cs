using System.Globalization;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

// The pure half of the provider quota lookup: which 9router accounts serve the nodes' models, and the parsing of
// 9router's dashboard API (GET /api/combos, /api/providers/client, /api/usage/{connectionId}).
public static class RouterQuotas
{
    // Limits shown for one account when none of them names a model the nodes use (lowest remaining first).
    public const int MaxUnmatchedWindows = 4;

    // Model ids from /v1/models carry the provider's alias ("cc/claude-opus-5-5") while connections carry the provider
    // id ("claude"). 9router exposes no endpoint for this map; these are the providers whose usage it can read.
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alims-intl"] = ["cbai"],
        ["antigravity"] = ["ag"],
        ["claude"] = ["cc"],
        ["cloudflare-ai"] = ["cf"],
        ["codex"] = ["cx"],
        ["commandcode"] = ["cmc"],
        ["deepseek"] = ["ds"],
        ["gemini-cli"] = ["gc"],
        ["github"] = ["gh"],
        ["grok-cli"] = ["gcli"],
        ["kiro"] = ["kr"],
        ["minimax-code"] = ["mm"],
        ["minimax-code-global"] = ["mmg", "br"],
        ["opencode-go"] = ["ocg"],
        ["opencode-zen"] = ["ocz"],
        ["qoder"] = ["qd"],
        ["qoder-cn"] = ["qdcn"],
        ["vercel-ai-gateway"] = ["vercel"],
        ["xiaomi-mimo"] = ["mimo"],
        ["zed"] = ["zd"]
    };

    // Model names (without the provider prefix) grouped by prefix; a combo counts as its member models.
    public static Dictionary<string, HashSet<string>> ModelsByPrefix(IEnumerable<string> models, IReadOnlyDictionary<string, IReadOnlyList<string>> combos)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in models)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var model = raw.Trim();
            foreach (var member in combos.TryGetValue(model, out var list) ? list : [model])
            {
                var slash = member.IndexOf('/');
                if (slash <= 0 || slash == member.Length - 1) continue;
                var prefix = member[..slash];
                if (!result.TryGetValue(prefix, out var names))
                    result[prefix] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                names.Add(member[(slash + 1)..]);
            }
        }
        return result;
    }

    // The models a provider serves for the nodes, or null when no node uses that provider.
    public static HashSet<string>? UsedModels(string provider, IReadOnlyDictionary<string, HashSet<string>> modelsByPrefix)
    {
        HashSet<string>? used = null;
        IEnumerable<string> names = Aliases.TryGetValue(provider, out var aliases) ? [provider, .. aliases] : [provider];
        foreach (var name in names)
            if (modelsByPrefix.TryGetValue(name, out var models))
                (used ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).UnionWith(models);
        return used;
    }

    // {"combos":[{"name":"x","models":["cc/a", {"model":"ag/b"}]}]} -> name -> member model ids.
    public static Dictionary<string, IReadOnlyList<string>> ParseCombos(string json)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (Property(JsonNode.Parse(json), "combos") is not JsonArray combos) return result;

        foreach (var combo in combos)
        {
            var name = String(Property(combo, "name")) ?? String(Property(combo, "id"));
            if (string.IsNullOrWhiteSpace(name) || Property(combo, "models") is not JsonArray members) continue;
            result[name] = members
                .Select(m => String(m) ?? String(Property(m, "model")) ?? String(Property(m, "id")))
                .OfType<string>()
                .Where(m => m.Length > 0)
                .ToList();
        }
        return result;
    }

    // Active accounts from GET /api/providers/client (the list without credentials).
    public static List<RouterConnection> ParseConnections(string json, out int totalPages)
    {
        var root = JsonNode.Parse(json);
        totalPages = Number(Property(Property(root, "pagination"), "totalPages")) is { } pages ? (int)pages : 1;

        var result = new List<RouterConnection>();
        if (Property(root, "connections") is not JsonArray connections) return result;
        foreach (var connection in connections)
        {
            var id = String(Property(connection, "id"));
            var provider = String(Property(connection, "provider"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(provider)) continue;
            if (Property(connection, "isActive") is JsonValue active && active.TryGetValue<bool>(out var isActive) && !isActive) continue;
            var name = String(Property(connection, "name")) ?? String(Property(connection, "email")) ?? provider;
            result.Add(new RouterConnection(id, provider, name));
        }
        return result;
    }

    // GET /api/usage/{connectionId}: {"plan":"Claude Code","quotas":{"session (5h)":{"remainingPercentage":17,
    // "resetAt":"…"}, …}}. When some limits name a model the nodes use (per-model quotas such as Antigravity's) only
    // those are kept. Null when there is nothing to show: API key accounts and unsupported providers answer
    // {"message": …}, failures {"error": …}.
    public static RouterQuotaDto? ParseUsage(string json, RouterConnection connection, IReadOnlySet<string> usedModels)
    {
        var root = JsonNode.Parse(json);
        if (Property(root, "quotas") is not JsonObject quotas) return null;

        var windows = new List<(RouterQuotaWindowDto Window, bool Used)>();
        foreach (var (key, value) in quotas)
        {
            if (value is not JsonObject quota) continue;
            var displayName = String(quota["displayName"]);
            var unlimited = quota["unlimited"] is JsonValue flag && flag.TryGetValue<bool>(out var isUnlimited) && isUnlimited;
            var window = new RouterQuotaWindowDto(
                displayName ?? key,
                unlimited ? null : RemainingPercent(quota),
                unlimited,
                Date(quota["resetAt"]));
            var used = usedModels.Contains(key) || (displayName is not null && usedModels.Contains(displayName));
            windows.Add((window, used));
        }
        if (windows.Count == 0) return null;

        var shown = windows.Any(w => w.Used)
            ? windows.Where(w => w.Used).Select(w => w.Window).ToList()
            : windows.Count <= MaxUnmatchedWindows
                ? windows.Select(w => w.Window).ToList()
                : windows.Select(w => w.Window).OrderBy(w => w.RemainingPercent ?? double.MaxValue).Take(MaxUnmatchedWindows).ToList();

        var plan = String(Property(root, "plan"));
        return new RouterQuotaDto(connection.Id, connection.Provider, connection.Name, string.IsNullOrWhiteSpace(plan) ? connection.Provider : plan, shown);
    }

    private static double? RemainingPercent(JsonObject quota)
    {
        var percent = Number(quota["remainingPercentage"]);
        if (percent is null && Number(quota["total"]) is { } total and > 0)
        {
            var remaining = Number(quota["remaining"]) ?? (Number(quota["used"]) is { } used ? total - used : null);
            if (remaining is not null) percent = remaining / total * 100;
        }
        return percent is { } p ? Math.Clamp(p, 0, 100) : null;
    }

    private static DateTime? Date(JsonNode? node) =>
        String(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : null;

    private static JsonNode? Property(JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name] : null;

    private static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }
}

// A 9router provider account.
public sealed record RouterConnection(string Id, string Provider, string Name);

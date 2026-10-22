using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

// AIThread's entries in "permissions.deny" of a Claude Code settings.json, written with the proxy settings: Claude Code
// itself never reads credential files (keys, certificates, token files, the profile folders that hold them), the same
// files SecretPaths keeps from AIT's own agents. Rules the user wrote are never touched; Remove takes back exactly
// these.
public static class ClaudePermissionRules
{
    // Anchored at the filesystem root ("//**/") so they also cover files outside the project.
    public static readonly IReadOnlyList<string> Rules =
    [
        "Read(//**/*.pem)",
        "Read(//**/*.key)",
        "Read(//**/*.pfx)",
        "Read(//**/*.p12)",
        "Read(//**/*.jks)",
        "Read(//**/*.keystore)",
        "Read(//**/*.kdbx)",
        "Read(//**/*.ppk)",
        "Read(//**/id_rsa)",
        "Read(//**/id_dsa)",
        "Read(//**/id_ecdsa)",
        "Read(//**/id_ed25519)",
        "Read(//**/.git-credentials)",
        "Read(//**/.npmrc)",
        "Read(//**/.pypirc)",
        "Read(//**/.netrc)",
        "Read(//**/.pgpass)",
        "Read(~/.ssh/**)",
        "Read(~/.aws/**)",
        "Read(~/.azure/**)",
        "Read(~/.gnupg/**)",
        "Read(~/.kube/**)",
        "Read(~/.docker/config.json)"
    ];

    public static void Apply(JsonObject settings)
    {
        var permissions = settings["permissions"] as JsonObject ?? new JsonObject();
        settings["permissions"] = permissions;
        var deny = permissions["deny"] as JsonArray ?? new JsonArray();
        permissions["deny"] = deny;

        var present = Values(deny).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in Rules.Where(r => !present.Contains(r))) deny.Add(rule);
    }

    public static void Remove(JsonObject settings)
    {
        if (settings["permissions"] is not JsonObject permissions || permissions["deny"] is not JsonArray deny) return;
        var ours = Rules.ToHashSet(StringComparer.Ordinal);
        for (var i = deny.Count - 1; i >= 0; i--)
            if (deny[i] is JsonValue value && value.TryGetValue<string>(out var rule) && ours.Contains(rule)) deny.RemoveAt(i);
        if (deny.Count == 0) permissions.Remove("deny");
        if (permissions.Count == 0) settings.Remove("permissions");
    }

    public static bool IsApplied(JsonObject settings) =>
        settings["permissions"]?["deny"] is JsonArray deny && Rules.All(Values(deny).ToHashSet(StringComparer.Ordinal).Contains);

    // The block as it is written, for the preview.
    public static JsonObject Block() => new() { ["deny"] = new JsonArray(Rules.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()) };

    private static IEnumerable<string> Values(JsonArray array) =>
        array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var text) ? text : null).OfType<string>();
}

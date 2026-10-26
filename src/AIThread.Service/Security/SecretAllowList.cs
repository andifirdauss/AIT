using System.Text.Json;

namespace AIThread.Service.Security;

// A value marked as not secret, as stored in SystemSetting.SecretAllowListJson: the id stands for the value (HMAC under
// this machine's key), label and preview only serve the list in Setting.
public sealed record SecretAllowEntry(string Id, string Label, string Preview, int Length, DateTime AllowedAt);

public static class SecretAllowList
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // An unreadable list counts as empty: values are masked again, which is the safe side.
    public static List<SecretAllowEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<SecretAllowEntry>>(json, Json) ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e.Id))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? Serialize(IReadOnlyCollection<SecretAllowEntry> entries) =>
        entries.Count == 0 ? null : JsonSerializer.Serialize(entries, Json);
}

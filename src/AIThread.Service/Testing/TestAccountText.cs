using System.Text;
using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Testing;

// The project's test accounts and testing settings as one value, read once per test run.
public sealed record TestAccountSet(IReadOnlyList<TestAccount> Accounts, TestingSettings Settings)
{
    public ImpersonationInfo? Impersonation => TestAccountText.ParseImpersonation(Settings.Impersonation);

    public TestAccount? Find(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : Accounts.FirstOrDefault(a => string.Equals(a.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

    // The accounts that may use "login as", in list order.
    public IReadOnlyList<TestAccount> Impersonators() =>
        Impersonation is { IsAvailable: true } info
            ? Accounts.Where(a => a.Role is not null && info.Roles.Contains(a.Role, StringComparer.OrdinalIgnoreCase)).ToList()
            : [];
}

// Parsing, validation and text of test accounts: the attributes JSON, the DTOs, and the "# Test accounts" section of
// the planner's and the tester's brief.
public static class TestAccountText
{
    public const int MaxAttributes = 30;
    public const int MaxAttributeKeyLength = 60;
    public const int MaxAttributeValueLength = 300;
    public const int MaxAccounts = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, string> ParseAttributes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    // Trimmed, empty pairs dropped, null when nothing is left. Keys keep the developer's spelling.
    public static string? SerializeAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var pairs = Clean(attributes);
        return pairs.Count == 0 ? null : JsonSerializer.Serialize(pairs, Json);
    }

    // Validates a request from the UI or an import; throws with the reason (Indonesian, for the developer).
    public static (string Username, string? Role, string? Attributes, string? Notes) Validate(SaveTestAccountRequest request)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (username.Length == 0) throw new AppValidationException("Username akun uji wajib diisi.");
        if (username.Length > TestAccount.MaxUsernameLength) throw new AppValidationException($"Username maksimal {TestAccount.MaxUsernameLength} karakter.");
        if (username.Any(char.IsWhiteSpace)) throw new AppValidationException("Username tidak boleh berisi spasi.");

        var role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim();
        if (role?.Length > TestAccount.MaxRoleLength) throw new AppValidationException($"Peran maksimal {TestAccount.MaxRoleLength} karakter.");

        var pairs = Clean(request.Attributes);
        if (pairs.Count > MaxAttributes) throw new AppValidationException($"Maksimal {MaxAttributes} atribut per akun.");
        foreach (var (key, value) in pairs)
        {
            if (key.Length > MaxAttributeKeyLength) throw new AppValidationException($"Nama atribut \"{key}\" maksimal {MaxAttributeKeyLength} karakter.");
            if (value.Length > MaxAttributeValueLength) throw new AppValidationException($"Nilai atribut \"{key}\" maksimal {MaxAttributeValueLength} karakter.");
        }
        var attributes = pairs.Count == 0 ? null : JsonSerializer.Serialize(pairs, Json);
        if (attributes?.Length > TestAccount.MaxAttributesLength) throw new AppValidationException("Atribut akun terlalu panjang.");

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > TestAccount.MaxNotesLength) throw new AppValidationException($"Catatan maksimal {TestAccount.MaxNotesLength} karakter.");
        return (username, role, attributes, notes);
    }

    public static TestAccountDto ToDto(TestAccount account) =>
        new(account.Id, account.Username, account.Role, ParseAttributes(account.Attributes), account.Notes, account.CreatedBy, account.UpdatedBy,
            account.CreatedAt, account.UpdatedAt);

    public static TestingSettingsDto ToDto(TestingSettings settings) =>
        new(settings.EffectiveDevPassword, string.IsNullOrEmpty(settings.DevPassword), settings.DevPasswordSource ?? TestingSettings.SourceDefault,
            ParseImpersonation(settings.Impersonation), settings.SideEffectsAcknowledgedBy, settings.SideEffectsAcknowledgedAt, settings.UpdatedBy, settings.UpdatedAt);

    public static ImpersonationInfo? ParseImpersonation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ImpersonationInfo>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? SerializeImpersonation(ImpersonationInfo? info) => info is null ? null : JsonSerializer.Serialize(info, Json);

    // The attribute keys the project's accounts use, most used first: suggestions for the next account.
    public static IReadOnlyList<string> AttributeKeys(IEnumerable<TestAccount> accounts) =>
        accounts.SelectMany(a => ParseAttributes(a.Attributes).Keys)
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    // "# Test accounts" for the planner and the tester. Null without accounts. The password itself never appears: the
    // tester writes {{password}} and AIT puts the project's development password in.
    public static string? Brief(TestAccountSet set, bool forTester)
    {
        if (set.Accounts.Count == 0) return null;
        var builder = new StringBuilder(forTester
            ? "# Test accounts (the project's shared list: switch_user to act as one, then log in with {{username}} and {{password}})\n"
            : "# Test accounts (the project's shared list; give a scenario an \"actor\" from it when the step needs a particular user)\n");
        foreach (var account in set.Accounts.Take(MaxAccounts))
        {
            builder.Append("- ").Append(account.Username);
            if (account.Role is not null) builder.Append(" · ").Append(account.Role);
            var attributes = ParseAttributes(account.Attributes);
            if (attributes.Count > 0) builder.Append(" · ").Append(string.Join(", ", attributes.Select(a => $"{a.Key} {a.Value}")));
            if (account.Notes is not null) builder.Append(" — ").Append(account.Notes.ReplaceLineEndings(" "));
            builder.Append('\n');
        }
        if (set.Accounts.Count > MaxAccounts) builder.Append($"- … {set.Accounts.Count - MaxAccounts} more accounts\n");

        if (forTester)
        {
            builder.Append(string.IsNullOrEmpty(set.Settings.DevPassword)
                ? "Password: the project has no development password set, so AIT uses a default one (development setups usually accept any password). If the app refuses it, call ask_login with reason \"dev password refused\".\n"
                : "Password: the project's development password (set by the developers).\n");
            if (set.Impersonation is { IsAvailable: true } info)
            {
                builder.Append($"Login as: {info.How}");
                if (!string.IsNullOrWhiteSpace(info.Back)) builder.Append($" Back to the admin: {info.Back}");
                builder.Append($" Roles that may use it: {string.Join(", ", info.Roles)}.");
                builder.Append(info.Confirmed ? string.Empty : " (detected by AIT, not confirmed by a developer yet)");
                builder.Append('\n');
            }
        }
        return builder.ToString();
    }

    // The account roles the planner may name as a scenario actor.
    public static IReadOnlyList<string> Roles(TestAccountSet set) =>
        set.Accounts.Select(a => a.Role).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? attributes)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
        {
            var k = key?.Trim() ?? string.Empty;
            var v = value?.Trim() ?? string.Empty;
            if (k.Length == 0 || v.Length == 0) continue;
            pairs[k] = v;
        }
        return pairs;
    }
}

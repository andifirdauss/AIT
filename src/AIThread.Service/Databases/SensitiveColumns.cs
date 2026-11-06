using System.Text.RegularExpressions;

namespace AIThread.Service.Databases;

// Columns whose values reach the model only as placeholders: the ones listed on the connection, plus names that
// usually hold personal data or secrets (email, phone, national id, password/hash/token, address, birth date, card or
// account numbers), in English and Indonesian.
public static partial class SensitiveColumns
{
    public static IReadOnlyList<string> Parse(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? []
            : list.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsSensitive(string column, IReadOnlyList<string> configured) =>
        configured.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase) || (c.EndsWith('*') && column.StartsWith(c[..^1], StringComparison.OrdinalIgnoreCase)))
        || DetectedPattern().IsMatch(column);

    // Label of the placeholder: "pii-<column>" with characters a placeholder label allows.
    public static string Label(string column)
    {
        var clean = LabelChars().Replace(column, "_");
        return "pii-" + (clean.Length > 40 ? clean[..40] : clean);
    }

    [GeneratedRegex(@"e_?mail|phone|mobile|telp|telepon|handphone|^hp$|_hp$|whatsapp|^wa$|nik$|^nik|ktp|npwp|passport|paspor|ssn|national_?id|password|passwd|pwd|pass_?hash|_hash$|^hash$|salt|token|secret|api_?key|private_?key|otp|pin$|address|alamat|birth|lahir|dob$|card_?(no|num)|credit_?card|cc_?num|cvv|rekening|account_?(no|num)|iban|bank_?acc|mother|ibu_?kandung|salary|gaji", RegexOptions.IgnoreCase)]
    private static partial Regex DetectedPattern();

    [GeneratedRegex(@"[^A-Za-z0-9_.\-]")]
    private static partial Regex LabelChars();
}

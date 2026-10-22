using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AIThread.Common.Entities;

namespace AIThread.Service.UnitTests;

// Small text helpers of the unit test catalog: list columns, readable titles from method names, and the input hash
// that lets a baseline reuse an earlier result of the same code.
public static partial class UnitTestText
{
    // Paths are kept one per line; other lists comma-separated. Both read either way.
    public static IReadOnlyList<string> Lines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string? JoinLines(IEnumerable<string>? values)
    {
        var list = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list is { Count: > 0 } ? string.Join("\n", list) : null;
    }

    public static string? JoinCsv(IEnumerable<string>? values)
    {
        var list = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list is { Count: > 0 } ? string.Join(",", list) : null;
    }

    // "Save_WithEmptyName_ReturnsError" → "Save with empty name returns error"; "test_add_numbers" → "Add numbers".
    // Titles of JavaScript tests are already sentences and stay as they are.
    public static string Humanize(string methodName)
    {
        var name = methodName.Trim('`').Trim();
        if (name.Contains(' ')) return name;
        if (name.StartsWith("test_", StringComparison.OrdinalIgnoreCase)) name = name[5..];
        else if (name.StartsWith("test", StringComparison.Ordinal) && name.Length > 4 && char.IsUpper(name[4])) name = name[4..];
        else if (name.StartsWith("Test", StringComparison.Ordinal) && name.Length > 4 && (char.IsUpper(name[4]) || name[4] == '_')) name = name[4..].TrimStart('_');
        var words = Words().Matches(name.Replace('_', ' ')).Select(m => m.Value).ToList();
        if (words.Count == 0) return methodName;
        var text = string.Join(" ", words.Select((w, i) => i == 0 ? w : IsAcronym(w) ? w : w.ToLowerInvariant()));
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    // The test file and the files it tests, as they are now: equal hashes mean the same code, so the same result.
    public static string InputHash(string root, UnitTestCase test)
    {
        using var sha = SHA256.Create();
        var builder = new StringBuilder();
        foreach (var file in new[] { test.TestFile }.Concat(Lines(test.TargetFiles)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(file.ToLowerInvariant()).Append(':');
            try
            {
                var full = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                builder.Append(File.Exists(full) ? Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(full))) : "missing");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                builder.Append("unreadable");
            }
            builder.Append('\n');
        }
        return Hash(builder.ToString());
    }

    // "UT-0012", "ut-12", "UT12" or "12" → "UT-0012"; null when it is not a test number.
    public static string? ParseNumber(string? text)
    {
        var match = Number().Match(text?.Trim() ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0 ? UnitTestCase.FormatNumber(number) : null;
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));

    public static string Clip(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static bool IsAcronym(string word) => word.Length > 1 && word.All(char.IsUpper);

    [GeneratedRegex(@"^(?:UT-?)?(\d{1,7})$", RegexOptions.IgnoreCase)]
    private static partial Regex Number();

    // Words of a PascalCase or camelCase name: "HTTPRequest2Fails" → HTTP, Request, 2, Fails.
    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex Words();
}

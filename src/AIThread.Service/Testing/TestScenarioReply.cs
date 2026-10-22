using System.Text.RegularExpressions;

namespace AIThread.Service.Testing;

// Chat replies about scenario sheets: "buat skenario test lanjutan 2" (make the sheet of suggestion 2) and
// "jalankan skenario approval-cuti" (run it; "... setuju" also accepts the warning about real data, asked once per
// project).
public static partial class TestScenarioReply
{
    public static int? ParseCreate(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        return Create().Match(reply.Trim()) is { Success: true } match && int.TryParse(match.Groups[1].Value, out var number) && number > 0 ? number : null;
    }

    public static (string Slug, bool Agreed)? ParseRun(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        return Run().Match(reply.Trim()) is { Success: true } match ? (match.Groups[1].Value.Trim('`', '"', '\''), match.Groups[2].Success) : null;
    }

    [GeneratedRegex(@"^buat(?:kan)?\s+(?:file\s+)?(?:skenario|scenario)(?:\s+excel)?\s+(?:(?:untuk|dari)\s+)?(?:test\s+lanjutan|follow[- ]?up)\s*(?:#|no\.?\s*|nomor\s*)?(\d{1,2})\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex Create();

    [GeneratedRegex(@"^(?:jalankan|run)\s+(?:skenario|scenario)\s+(`?[A-Za-z0-9][A-Za-z0-9\-_.]{0,90}`?)(\s+(?:setuju|ya|ok|agree))?\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex Run();
}

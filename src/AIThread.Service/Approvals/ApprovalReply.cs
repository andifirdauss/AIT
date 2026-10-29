using System.Text.RegularExpressions;
using AIThread.Common.Enums;

namespace AIThread.Service.Approvals;

// The developer's answer to an approval request (ApprovalRequest.ToText), in either language. Anything else is not an
// answer: a paused planner then reads it as a denial with a new instruction.
public static partial class ApprovalReply
{
    public static ApprovalDecision? Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var text = reply.Trim();
        if (OnceRegex().IsMatch(text)) return ApprovalDecision.Once;
        if (AlwaysRegex().IsMatch(text)) return ApprovalDecision.Always;
        if (DenyRegex().IsMatch(text)) return ApprovalDecision.Deny;
        if (PatternRegex().IsMatch(text)) return ApprovalDecision.AlwaysPattern;
        if (AllRegex().IsMatch(text)) return ApprovalDecision.AlwaysAll;
        return null;
    }

    [GeneratedRegex(@"^(?:1|(?:izinkan\s+|boleh\s+|allow\s+)?(?:sekali(?:\s+saja)?|once|hanya\s+sekali|just\s+once))\s*[.!)]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OnceRegex();

    [GeneratedRegex(@"^(?:2|(?:izinkan\s+|boleh\s+)?(?:selalu|berkelanjutan|seterusnya)|(?:always(?:\s+allow)?|allow\s+always))\s*[.!)]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AlwaysRegex();

    [GeneratedRegex(@"^(?:3|tolak|jangan|tidak|no|deny|denied|cancel|batal)\s*[.!)]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DenyRegex();

    // "4", "pola", "izinkan pola", "selalu untuk pola", "pattern", "allow pattern", "always pattern", "always allow the pattern".
    [GeneratedRegex(@"^(?:4|(?:izinkan\s+)?(?:selalu\s+)?(?:untuk\s+)?pola|(?:always\s+)?(?:allow\s+)?(?:the\s+)?pattern)\s*[.!)]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PatternRegex();

    // "5", "semua", "izinkan semua perintah", "selalu semua", "all", "allow all", "always allow all commands".
    [GeneratedRegex(@"^(?:5|(?:izinkan\s+)?(?:selalu\s+)?semua(?:\s+perintah)?|(?:always\s+)?(?:allow\s+)?all(?:\s+commands)?)\s*[.!)]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AllRegex();
}

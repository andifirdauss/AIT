using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Workspace.Structure;

// A function or method with a body: what the call scanner reads. Symbol is "Type.Member" (with the outer type of a
// nested one) or the bare name of a function outside any type.
public sealed record FunctionSpan(string Symbol, string Name, string? Signature, int Line, int EndLine)
{
    public bool Overlaps(int from, int to) => Line <= to && from <= EndLine;
}

public static class FunctionSpans
{
    private static readonly HashSet<string> CallableKinds = new(StringComparer.Ordinal) { "method", "constructor", "function" };

    public static IReadOnlyList<FunctionSpan> Of(FileOutline outline)
    {
        var spans = new List<FunctionSpan>();
        foreach (var type in outline.Types)
            spans.AddRange(type.Members.Where(IsScanned).Select(m => new FunctionSpan(SymbolOf(type.Name, type.Container, m.Name), m.Name, m.Signature, m.Line, m.EndLine)));
        spans.AddRange(outline.Functions.Where(IsScanned).Select(f => new FunctionSpan(f.Name, f.Name, f.Signature, f.Line, f.EndLine)));
        return spans;
    }

    // Methods, constructors and functions with a body of their own (a one-line declaration has nothing to read).
    public static bool IsScanned(OutlineMember member) => CallableKinds.Contains(member.Kind) && member.EndLine > member.Line;

    public static string SymbolOf(string? typeName, string? container, string member) =>
        typeName is null ? member : container is null ? $"{typeName}.{member}" : $"{container}.{typeName}.{member}";

    // Stable across scans while the function keeps its file, name and signature (its lines may move).
    public static string Key(string path, string symbol, string? signature) => Hash($"{path.Replace('\\', '/')}|{symbol}|{signature}");

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // The 1-based line range that differs between two versions of a file (one block from the first to the last
    // changed line of the new version); null when nothing changed.
    public static (int From, int To)? ChangedRange(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix]) prefix++;
        if (prefix == before.Count && prefix == after.Count) return null;
        var suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var from = prefix + 1;
        var to = Math.Max(from, after.Count - suffix);
        return (from, to);
    }
}

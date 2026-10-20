using AIThread.Common.Enums;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    // Evidence: a URL's literal path parts appear in the body; any other call's own name is on its line or a line next
    // to it (the model may count a wrapped call from its first line).
    private static bool Written(CodeCallKind kind, string target, int line, string[] lines, string body)
    {
        if (kind == CodeCallKind.Http)
        {
            var literal = HttpPrefix().Replace(target, string.Empty);
            var parts = literal.Split(['/', '?', '{', '}'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 1).ToList();
            return parts.Count > 0 && parts.All(p => body.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
        var name = LastName().Match(target.Split('(')[0]) is { Success: true } m ? m.Value : target;
        for (var i = Math.Max(1, line - 1); i <= Math.Min(lines.Length, line + 1); i++)
            if (lines[i - 1].Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

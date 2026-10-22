using System.Text;
using System.Text.RegularExpressions;
using AIThread.Service.Workspace;

namespace AIThread.Service.Testing;

// One place in the code that looks like "login as": the line and a few lines around it, where the role guard of the
// action (an [Authorize(Roles = ...)] attribute, an IsInRole check) usually sits.
public sealed record ImpersonationHit(string File, int Line, string Excerpt);

// Finds "login as" (an admin entering the app as another user) in a project's code without a model: names such as
// Impersonate, LoginAs, SwitchUser, RunAs, SignInAs, "login sebagai". The cheap model then reads the hits
// (ImpersonationDetector). Generated and third-party folders are skipped (WorkspaceScope), and so are protected files.
public static partial class ImpersonationScanner
{
    public const int MaxHits = 25;
    private const int MaxFileBytes = 512 * 1024;
    private const int ContextLines = 4;
    private const int MaxExcerptChars = 900;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".cshtml", ".razor", ".aspx", ".ascx", ".vb", ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".php", ".py",
        ".java", ".kt", ".go", ".rb", ".html"
    };

    public static IReadOnlyList<ImpersonationHit> Scan(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var hits = new List<ImpersonationHit>();
        foreach (var path in scope.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(path)) || scope.IsProtected(path)) continue;
            string[] lines;
            try
            {
                if (new FileInfo(path).Length > MaxFileBytes) continue;
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (!Pattern().IsMatch(lines[i])) continue;
                hits.Add(new ImpersonationHit(scope.ToRelative(path), i + 1, Excerpt(lines, i)));
                if (hits.Count >= MaxHits) return hits;
                // One hit per block: the lines shown already cover the next few matches.
                i += ContextLines;
            }
        }
        return hits;
    }

    private static string Excerpt(string[] lines, int index)
    {
        var builder = new StringBuilder();
        for (var i = Math.Max(0, index - ContextLines); i <= Math.Min(lines.Length - 1, index + ContextLines); i++)
            builder.Append(i + 1).Append(": ").Append(lines[i].TrimEnd()).Append('\n');
        var text = builder.ToString();
        return text.Length <= MaxExcerptChars ? text : text[..MaxExcerptChars] + "…";
    }

    [GeneratedRegex(@"impersonat|\blogin[_\- ]?as\b|loginas|\bswitch[_\-]?user|\brun[_\-]?as\b|\bsign[_\-]?in[_\-]?as\b|become[_\-]?user|login\s+sebagai|masuk\s+sebagai|\bsudo[_\-]?login", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}

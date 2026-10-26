using AIThread.Common.DTOs;

namespace AIThread.Service.Business;

// A line diff of two Markdown pages for the wiki compare: longest common subsequence, unchanged runs folded to a few
// lines of context. Pages too large for that are shown as removed + added.
public static class TextDiff
{
    private const int Context = 3;
    private const long MaxCells = 4_000_000;

    public static IReadOnlyList<BusinessDiffLineDto> Lines(string left, string right)
    {
        var a = Split(left);
        var b = Split(right);
        var lines = (long)a.Length * b.Length <= MaxCells
            ? Lcs(a, b)
            : a.Select(l => new BusinessDiffLineDto("del", l)).Concat(b.Select(l => new BusinessDiffLineDto("add", l))).ToList();
        return Fold(lines);
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static List<BusinessDiffLineDto> Lcs(string[] a, string[] b)
    {
        // length[i, j] = common lines of a[i..] and b[j..].
        var length = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                length[i, j] = a[i] == b[j] ? length[i + 1, j + 1] + 1 : Math.Max(length[i + 1, j], length[i, j + 1]);

        var result = new List<BusinessDiffLineDto>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add(new BusinessDiffLineDto("same", a[x]));
                x++;
                y++;
            }
            else if (length[x + 1, y] >= length[x, y + 1]) result.Add(new BusinessDiffLineDto("del", a[x++]));
            else result.Add(new BusinessDiffLineDto("add", b[y++]));
        }
        while (x < a.Length) result.Add(new BusinessDiffLineDto("del", a[x++]));
        while (y < b.Length) result.Add(new BusinessDiffLineDto("add", b[y++]));
        return result;
    }

    // Unchanged runs keep Context lines next to a change; the rest becomes one "skip" line.
    private static IReadOnlyList<BusinessDiffLineDto> Fold(List<BusinessDiffLineDto> lines)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == "same") continue;
            for (var k = Math.Max(0, i - Context); k <= Math.Min(lines.Count - 1, i + Context); k++) keep[k] = true;
        }

        var result = new List<BusinessDiffLineDto>();
        var skipped = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                if (skipped > 0) result.Add(new BusinessDiffLineDto("skip", skipped.ToString()));
                skipped = 0;
                result.Add(lines[i]);
            }
            else skipped++;
        }
        if (skipped > 0) result.Add(new BusinessDiffLineDto("skip", skipped.ToString()));
        return result;
    }
}

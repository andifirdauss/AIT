using System.Text;

namespace AIThread.Service.Workspace;

// Content is always handled with "\n" line endings in memory; the original BOM and line ending
// style are restored on write so edits do not produce whole-file diffs.
public sealed record TextFileState(bool Exists, string Content, bool HasBom, string NewLine)
{
    public int LineCount => WorkspaceTools.SplitLines(Content).Length;
}

public static class TextFileIo
{
    public static async Task<TextFileState> ReadAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fullPath)) return new TextFileState(false, string.Empty, false, Environment.NewLine);

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;
        var text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : Environment.NewLine;
        return new TextFileState(true, NormalizeNewLines(text), hasBom, newLine);
    }

    public static async Task WriteAsync(string fullPath, string content, TextFileState original, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var normalized = NormalizeNewLines(content);
        var text = original.NewLine == "\n" ? normalized : normalized.Replace("\n", original.NewLine, StringComparison.Ordinal);
        await File.WriteAllTextAsync(fullPath, text, new UTF8Encoding(original.HasBom), cancellationToken);
    }

    public static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static bool LooksBinary(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            Span<byte> buffer = stackalloc byte[4096];
            var read = stream.Read(buffer);
            return buffer[..read].IndexOf((byte)0) >= 0;
        }
        catch (IOException)
        {
            return true;
        }
    }
}

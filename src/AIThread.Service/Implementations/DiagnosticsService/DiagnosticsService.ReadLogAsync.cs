using System.Text;
using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    // The last tail lines of a run log; with search, the last tail lines containing it (an indented continuation line
    // such as a stack trace frame follows its header line).
    public async Task<LogFileTailDto?> ReadLogAsync(string name, int tail, string? search, CancellationToken cancellationToken = default)
    {
        var file = new DirectoryInfo(LogRoot).Exists
            ? new DirectoryInfo(LogRoot).GetFiles("*.log").FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
        if (file is null) return null;

        var take = Math.Clamp(tail, 50, 5000);
        var text = search?.Trim();
        var lines = new Queue<string>();
        var truncated = false;
        var keep = false;
        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrEmpty(text))
            {
                var continuation = line.StartsWith(' ') || line.StartsWith('\t');
                keep = continuation ? keep : line.Contains(text, StringComparison.OrdinalIgnoreCase);
                if (!keep) continue;
            }
            lines.Enqueue(line);
            if (lines.Count <= take) continue;
            lines.Dequeue();
            truncated = true;
        }
        return new LogFileTailDto(file.Name, lines.Select(DiagnosticText.Mask).ToList(), truncated);
    }
}

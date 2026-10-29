using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    public string? Record(DiagnosticReport report)
    {
        try
        {
            var now = DateTime.UtcNow;
            var innermost = report.Exception is null ? null : DiagnosticText.Innermost(report.Exception);
            var exceptionType = innermost?.GetType().FullName;
            var title = Line(innermost?.Message ?? report.Title);
            var key = DiagnosticText.FoldKey(report.Category, exceptionType, title);

            lock (_gate)
            {
                if (_recent.TryGetValue(key, out var seen) && now - seen.At < FoldWindow
                    && IncidentFolders.FolderOf(IncidentsRoot, seen.Id) is { } folder && IncidentFolders.Read(folder) is { } earlier)
                {
                    earlier.Count++;
                    earlier.LastAt = now;
                    if (!earlier.Categories.Contains(report.Category)) earlier.Categories.Add(report.Category);
                    earlier.ThreadId ??= report.ThreadId;
                    earlier.ProjectId ??= report.ProjectId;
                    IncidentFolders.Write(folder, earlier);
                    _recent[key] = (seen.Id, now);
                    return seen.Id;
                }

                var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
                var target = Path.Combine(IncidentsRoot, id);
                IncidentFolders.Write(target, new DiagnosticIncident
                {
                    Id = id,
                    CreatedAt = now,
                    LastAt = now,
                    Level = report.Level,
                    Categories = [report.Category],
                    Title = innermost is null ? title : $"{innermost.GetType().Name}: {title}",
                    Message = report.Message is null ? null : DiagnosticText.Mask(report.Message),
                    ThreadId = report.ThreadId,
                    ProjectId = report.ProjectId,
                    ExceptionType = exceptionType,
                    StackTrace = report.Exception is null ? null : DiagnosticText.Mask(report.Exception.ToString()),
                    AppVersion = DiagnosticText.AppVersion
                });
                WriteFiles(target, report.Files);
                _recent[key] = (id, now);
                if (_recent.Count > 500) _recent.Clear();
                if (++_sinceprune >= 20)
                {
                    _sinceprune = 0;
                    Prune(now);
                }

                eventBus.Publish(AppEvent.Create(AppEventTypes.IncidentRecorded, new { Id = id }, report.ThreadId));
                return id;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void WriteFiles(string folder, IReadOnlyList<DiagnosticAttachment>? files)
    {
        if (files is not { Count: > 0 }) return;
        var target = Directory.CreateDirectory(Path.Combine(folder, IncidentFolders.FilesFolder)).FullName;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = DiagnosticText.SafeName(file.Name);
            for (var n = 2; !used.Add(name); n++) name = $"{Path.GetFileNameWithoutExtension(file.Name)}-{n}{Path.GetExtension(file.Name)}";
            File.WriteAllText(Path.Combine(target, name), DiagnosticText.Mask(DiagnosticText.Tail(file.Content, MaxAttachmentChars)));
        }
    }

    private static string Line(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 300 ? line : line[..300] + "…";
    }
}

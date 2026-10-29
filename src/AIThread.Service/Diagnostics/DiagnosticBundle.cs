using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AIThread.Service.Diagnostics;

// What goes into a diagnostic ZIP. ActivityText: the activity log as text (null when the database is not at hand, as
// in the offline CLI export). Source: who made it ("web", "cli", "cli-offline").
public sealed record DiagnosticBundleInput(
    string IncidentsRoot,
    string LogRoot,
    int Days,
    bool Incidents,
    bool RunLogs,
    Guid? ThreadId,
    string? ActivityText,
    string Source);

// Writes the diagnostic ZIP a developer brings to whoever maintains AIT:
//   README.md                 what is inside, the AIT version, the machine (OS, .NET)
//   activity.log              the activity log of the period (Dashboard log)
//   logs/*.log                the run logs of AIThread.exe and 9router
//   incidents/<id>/incident.md, incident.json, files/*   errors with stack trace and companion files
// Every text is masked (DiagnosticText.Mask). Used by IDiagnosticsService and, without a server, by the CLI.
public static class DiagnosticBundle
{
    public static string FileName(DateTime now) => $"ait-diagnostics-{Environment.MachineName.ToLowerInvariant()}-{now:yyyyMMdd-HHmm}.zip";

    public static async Task<(int Incidents, int Logs)> WriteAsync(Stream output, DiagnosticBundleInput input, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(input.Days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);
        var incidents = 0;
        var logs = 0;

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (input.ActivityText is not null) await AddTextAsync(zip, "activity.log", DiagnosticText.Mask(input.ActivityText), cancellationToken);

            if (input.RunLogs && Directory.Exists(input.LogRoot))
            {
                foreach (var file in new DirectoryInfo(input.LogRoot).GetFiles("*.log").Where(f => f.LastWriteTimeUtc >= since).OrderBy(f => f.Name))
                {
                    var text = await ReadSharedAsync(file.FullName, cancellationToken);
                    if (text is null) continue;
                    await AddTextAsync(zip, $"logs/{DiagnosticText.SafeName(file.Name)}", DiagnosticText.Mask(DiagnosticText.Tail(text)), cancellationToken);
                    logs++;
                }
            }

            if (input.Incidents)
            {
                foreach (var (incident, folder) in IncidentFolders.Load(input.IncidentsRoot))
                {
                    if (incident.LastAt < since) continue;
                    if (input.ThreadId is { } thread && incident.ThreadId != thread) continue;
                    var files = IncidentFolders.Files(folder);
                    var prefix = $"incidents/{incident.Id}";
                    await AddTextAsync(zip, $"{prefix}/incident.md", DiagnosticText.Mask(DiagnosticText.Markdown(incident, files.Select(f => f.Name))), cancellationToken);
                    await AddTextAsync(zip, $"{prefix}/incident.json", DiagnosticText.Mask(JsonSerializer.Serialize(incident, new JsonSerializerOptions { WriteIndented = true })), cancellationToken);
                    foreach (var file in files)
                        if (await ReadSharedAsync(file.FullName, cancellationToken) is { } text)
                            await AddTextAsync(zip, $"{prefix}/files/{file.Name}", DiagnosticText.Mask(text), cancellationToken);
                    incidents++;
                }
            }

            await AddTextAsync(zip, "README.md", Readme(input, days, incidents, logs), cancellationToken);
        }
        return (incidents, logs);
    }

    private static string Readme(DiagnosticBundleInput input, int days, int incidents, int logs)
    {
        var builder = new StringBuilder("# AIT diagnostics\n\n");
        builder.Append("- Created: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append('\n');
        builder.Append("- AIT: ").Append(DiagnosticText.AppVersion).Append(" (").Append(input.Source).Append(")\n");
        builder.Append("- OS: ").Append(RuntimeInformation.OSDescription).Append(" · ").Append(RuntimeInformation.FrameworkDescription).Append('\n');
        builder.Append("- Period: last ").Append(days).Append(" day(s)");
        if (input.ThreadId is { } thread) builder.Append(" · thread ").Append(thread);
        builder.Append('\n');
        builder.Append("- Incidents: ").Append(incidents).Append(" · run logs: ").Append(logs)
            .Append(" · activity log: ").Append(input.ActivityText is null ? "not included" : "activity.log").Append("\n\n");
        builder.Append("Secrets matching AIT's secret patterns (passwords, API keys, tokens, connection-string parts) are replaced by ");
        builder.Append("`[secret:<label>]`. Source code, file paths and prompts can still be inside: share this file only with people who may see them.\n\n");
        builder.Append("Start with `incidents/*/incident.md` (error, stack trace, companion files), then `activity.log` and `logs/` around the same time.\n");
        return builder.ToString();
    }

    private static async Task AddTextAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }

    // The current run log is still being written: open it shared.
    private static async Task<string?> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > DiagnosticText.MaxFileChars * 2L) stream.Seek(-DiagnosticText.MaxFileChars * 2L, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

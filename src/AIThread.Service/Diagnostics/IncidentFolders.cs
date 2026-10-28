using System.Text;
using System.Text.Json;

namespace AIThread.Service.Diagnostics;

// The incidents on disk: one folder per incident, <root>/<id>/incident.json plus <root>/<id>/files/*. Kept as files so
// the export also works when the database or the server does not (AIThread.exe logs export).
public static class IncidentFolders
{
    public const string IncidentFile = "incident.json";
    public const string FilesFolder = "files";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // %LOCALAPPDATA%\AIThread\<name>, or the configured folder.
    public static string Resolve(string? configured, string name) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", name)
            : Path.GetFullPath(configured);

    // Newest first; a folder whose incident.json cannot be read is skipped.
    public static IEnumerable<(DiagnosticIncident Incident, string Path)> Load(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var folder in Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            var incident = Read(folder);
            if (incident is not null) yield return (incident, folder);
        }
    }

    public static DiagnosticIncident? Read(string folder)
    {
        try
        {
            var file = Path.Combine(folder, IncidentFile);
            return File.Exists(file) ? JsonSerializer.Deserialize<DiagnosticIncident>(File.ReadAllText(file, Encoding.UTF8), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string folder, DiagnosticIncident incident)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, IncidentFile), JsonSerializer.Serialize(incident, Json), new UTF8Encoding(false));
    }

    public static IReadOnlyList<FileInfo> Files(string folder)
    {
        var directory = new DirectoryInfo(Path.Combine(folder, FilesFolder));
        return directory.Exists ? directory.GetFiles().OrderBy(f => f.Name, StringComparer.Ordinal).ToList() : [];
    }

    // Ids are folder names AIT made itself; anything else (a path, "..") is refused.
    public static string? FolderOf(string root, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !(char.IsLetterOrDigit(c) || c == '-'))) return null;
        var folder = Path.Combine(root, id);
        return Directory.Exists(folder) ? folder : null;
    }
}

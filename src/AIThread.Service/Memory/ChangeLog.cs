using System.Text;
using System.Text.Json.Nodes;
using AIThread.Service.Skills;

namespace AIThread.Service.Memory;

// A file in a change entry: what the thread did to it (ChangeLog.Created/Modified/Deleted) and, from the model, what
// changed in it. In a module summary Action is empty and Note is the file's role in the module.
public sealed record ChangeLogFile(string Path, string? Action, string? Note);

// One change entry (MemoryKind.Change) as the extractor's model describes it. The file list comes from the files the
// thread really changed, never from the model.
public sealed record ChangeLogEntry(
    string Title,
    string? Feature,
    IReadOnlyList<ChangeLogFile> Files,
    IReadOnlyList<string> Validation,
    IReadOnlyList<string> Flow,
    IReadOnlyList<string> Notes);

// The current state of one module (MemoryKind.Summary).
public sealed record ChangeLogState(
    IReadOnlyList<string> Features,
    IReadOnlyList<ChangeLogFile> Files,
    IReadOnlyList<string> Validation,
    IReadOnlyList<string> Flow,
    IReadOnlyList<string> Notes);

// The MEMORY.md-like text of project memory: AIT renders every change entry and module summary from the model's JSON,
// so all of them read the same way for the developer in the Memory menu and for the planner of a later thread. The
// headings follow the language the text is written in (english: TextLanguage.English), not the UI language: memory is
// shared with a team whose members may use AIT in another language.
public static class ChangeLog
{
    // What the thread did to a file; rendered as baru/ubah/hapus or new/modified/deleted.
    public const string Created = "created";
    public const string Modified = "modified";
    public const string Deleted = "deleted";

    public const int MaxTitleChars = 120;
    public const int MaxEntryFiles = 40;
    public const int MaxSummaryFiles = 30;
    public const int MaxEntryChars = 4_000;
    public const int MaxSummaryChars = 8_000;

    private const int MaxLines = 12;
    private const int MaxSummaryLines = 25;
    private const int MaxLineChars = 400;

    // Null when the model gave no title (the entry is skipped).
    public static ChangeLogEntry? ParseEntry(JsonObject? item)
    {
        if (item is null || Line(SkillAi.Text(item["title"]), MaxTitleChars) is not { } title) return null;
        var files = new List<ChangeLogFile>();
        foreach (var file in (item["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (SkillAi.Text(file["path"]) is not { } path) continue;
            files.Add(new ChangeLogFile(NormalizePath(path), null, Line(SkillAi.Text(file["change"]), MaxLineChars)));
        }
        return new ChangeLogEntry(title, Line(SkillAi.Text(item["feature"]), MaxLineChars * 2), files,
            Lines(item["validation"], MaxLines), Lines(item["flow"], MaxLines), Lines(item["notes"], MaxLines));
    }

    public static ChangeLogState ParseState(JsonObject? item)
    {
        var files = new List<ChangeLogFile>();
        foreach (var file in (item?["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (SkillAi.Text(file["path"]) is not { } path) continue;
            files.Add(new ChangeLogFile(NormalizePath(path), null, Line(SkillAi.Text(file["role"]), MaxLineChars)));
        }
        return new ChangeLogState(Lines(item?["features"], MaxSummaryLines), files.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(MaxSummaryFiles).ToList(),
            Lines(item?["validation"], MaxSummaryLines), Lines(item?["flow"], MaxSummaryLines), Lines(item?["notes"], MaxSummaryLines));
    }

    // The thread's changed files with what the model said about each; a changed file the model left out is still listed,
    // a file the model named that the thread did not change is dropped.
    public static IReadOnlyList<ChangeLogFile> MergeFiles(IReadOnlyList<ChangeLogFile> changed, IReadOnlyList<ChangeLogFile> described)
    {
        var notes = described
            .Where(d => d.Note is not null)
            .GroupBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Note, StringComparer.OrdinalIgnoreCase);
        return changed.Select(c => c with { Note = notes.GetValueOrDefault(c.Path) ?? c.Note }).Take(MaxEntryFiles).ToList();
    }

    public static string Render(ChangeLogEntry entry, bool english = false)
    {
        var builder = new StringBuilder();
        builder.Append("### ").Append(entry.Title).Append('\n');
        if (entry.Feature is not null) builder.Append(english ? "**Feature:** " : "**Fitur:** ").Append(entry.Feature).Append('\n');
        AppendFiles(builder, english ? "Changed files" : "File berubah", entry.Files, english);
        AppendList(builder, english ? "Validation" : "Validasi", entry.Validation, numbered: false);
        AppendList(builder, "Flow", entry.Flow, numbered: true);
        AppendList(builder, english ? "Notes" : "Catatan", entry.Notes, numbered: false);
        return Clip(builder.ToString().TrimEnd(), MaxEntryChars);
    }

    public static string Render(string module, ChangeLogState state, bool english = false)
    {
        var builder = new StringBuilder();
        builder.Append("### ").Append(module).Append(english ? " — current state\n" : " — kondisi saat ini\n");
        AppendList(builder, english ? "Features" : "Fitur", state.Features, numbered: false);
        AppendFiles(builder, english ? "Main files" : "File utama", state.Files, english);
        AppendList(builder, english ? "Validation" : "Validasi", state.Validation, numbered: false);
        AppendList(builder, "Flow", state.Flow, numbered: false);
        AppendList(builder, english ? "Notes" : "Catatan", state.Notes, numbered: false);
        return Clip(builder.ToString().TrimEnd(), MaxSummaryChars);
    }

    // The "### " headline Render writes on the first line of a change entry; null for other text.
    public static string? Title(string? description)
    {
        if (description is null || !description.StartsWith("### ", StringComparison.Ordinal)) return null;
        var end = description.IndexOf('\n');
        return Line(end < 0 ? description[4..] : description[4..end], MaxTitleChars);
    }

    public static bool IsEmpty(ChangeLogState state) =>
        state.Features.Count + state.Files.Count + state.Validation.Count + state.Flow.Count + state.Notes.Count == 0;

    // Forward slashes, no leading "./" or "/".
    public static string NormalizePath(string path) => path.Trim().Trim('`').Replace('\\', '/').TrimStart('.', '/');

    private static void AppendFiles(StringBuilder builder, string heading, IReadOnlyList<ChangeLogFile> files, bool english)
    {
        if (files.Count == 0) return;
        builder.Append("\n**").Append(heading).Append(":**\n");
        foreach (var file in files)
        {
            builder.Append("- `").Append(file.Path).Append('`');
            if (!string.IsNullOrEmpty(file.Action)) builder.Append(" (").Append(ActionText(file.Action, english)).Append(')');
            if (file.Note is not null) builder.Append(" — ").Append(file.Note);
            builder.Append('\n');
        }
    }

    private static void AppendList(StringBuilder builder, string heading, IReadOnlyList<string> lines, bool numbered)
    {
        if (lines.Count == 0) return;
        builder.Append("\n**").Append(heading).Append(":**\n");
        for (var i = 0; i < lines.Count; i++)
            builder.Append(numbered ? $"{i + 1}. " : "- ").Append(lines[i]).Append('\n');
    }

    private static string ActionText(string action, bool english) => action switch
    {
        Created => english ? "new" : "baru",
        Modified => english ? "modified" : "ubah",
        Deleted => english ? "deleted" : "hapus",
        _ => action
    };

    // Strings of a JSON array (a single string counts as one), one line each, without the model's own bullets.
    private static IReadOnlyList<string> Lines(JsonNode? node, int max)
    {
        var values = node is JsonArray array ? array.Select(v => SkillAi.Text(v)) : [SkillAi.Text(node)];
        return values
            .Select(v => Line(v, MaxLineChars))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private static string? Line(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimStart('-', '*', '•', ' ');
        if (line.Length > 3 && char.IsDigit(line[0]) && line.IndexOf(". ", StringComparison.Ordinal) is > 0 and < 4 and var dot) line = line[(dot + 2)..];
        return line.Length == 0 ? null : Clip(line, max);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AIThread.Service.Security;

namespace AIThread.Service.Diagnostics;

// Text helpers of incidents and the export: masking, exception text, file names.
public static partial class DiagnosticText
{
    // A companion file or log larger than this keeps only its end (the part nearest the error).
    public const int MaxFileChars = 2_000_000;

    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    // Everything that leaves the machine goes through the secret patterns (passwords, keys, tokens, connection-string
    // parts); the value becomes [secret:<label>], never a placeholder AIT could turn back.
    public static string Mask(string text) =>
        SecretRules.Redact(text, config: false, null, (label, _) => $"[secret:{label}]");

    // The innermost exception: the one a developer needs ("Cannot insert duplicate key ..." under DbUpdateException).
    public static Exception Innermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null) current = current.InnerException;
        return current;
    }

    // Same error = same innermost type and message (numbers and ids folded, they differ per occurrence).
    public static string FoldKey(string category, string? exceptionType, string title) =>
        $"{exceptionType ?? category}\n{Digits().Replace(title, "#")}";

    public static string Tail(string text, int maxChars = MaxFileChars) =>
        text.Length <= maxChars ? text : "…(truncated)\n" + text[^maxChars..];

    // A name safe for a file in the incident folder and in the ZIP.
    public static string SafeName(string name)
    {
        var clean = new StringBuilder();
        foreach (var c in Path.GetFileName(name.Trim()))
            clean.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        var text = clean.ToString().Trim('.');
        return text.Length == 0 ? "file.txt" : text.Length > 100 ? text[..100] : text;
    }

    // incident.md: the incident as a person reads it, in the export.
    public static string Markdown(DiagnosticIncident incident, IEnumerable<string> files)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(incident.Title).Append("\n\n");
        builder.Append("- Id: ").Append(incident.Id).Append('\n');
        builder.Append("- Level: ").Append(incident.Level).Append('\n');
        builder.Append("- Category: ").Append(string.Join(", ", incident.Categories)).Append('\n');
        builder.Append("- First: ").Append(incident.CreatedAt.ToString("u")).Append(" · last: ").Append(incident.LastAt.ToString("u"))
            .Append(" · count: ").Append(incident.Count).Append('\n');
        if (incident.ThreadId is { } thread) builder.Append("- Thread: ").Append(thread).Append('\n');
        if (incident.ProjectId is { } project) builder.Append("- Project: ").Append(project).Append('\n');
        if (incident.AppVersion is not null) builder.Append("- AIT: ").Append(incident.AppVersion).Append('\n');
        if (!string.IsNullOrWhiteSpace(incident.Message)) builder.Append("\n## Message\n\n").Append(incident.Message.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(incident.StackTrace))
            builder.Append("\n## Exception\n\n```\n").Append(incident.StackTrace.Trim()).Append("\n```\n");
        var names = files.ToList();
        if (names.Count > 0) builder.Append("\n## Files\n\n").Append(string.Join('\n', names.Select(n => $"- files/{n}"))).Append('\n');
        return builder.ToString();
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}

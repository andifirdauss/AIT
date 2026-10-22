using System.Text;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

// An earlier AIT thread of this chat that changed files (a rolled-back one does not count).
public sealed record SessionChangeThread(Guid ThreadId, string Prompt, DateTime At, IReadOnlyList<string> Files);

// What the chat changed in one module: its threads newest first, and their files.
public sealed record SessionModuleChange(string Module, IReadOnlyList<SessionChangeThread> Threads, IReadOnlyList<string> Files)
{
    public DateTime LatestAt => Threads[0].At;
}

// The "# Changes in this session" section of the planner's brief: what "this change" can refer to in a follow-up such as
// "test this change". The chat's own threads, not the whole git diff: another session may be changing the same folder.
public static class SessionChanges
{
    // Threads looked back at, and modules a question offers (one more option takes them all).
    public const int MaxThreads = 10;
    public const int MaxAskedModules = 3;

    // Files the code map has no module for yet are grouped per thread ("(no module) thread 1a2b3c4d"): one bucket for all
    // of them would merge changes of different features into one choice.
    public const string UnmappedPrefix = "(no module)";

    private const int MaxListedFiles = 6;
    private const int MaxListedTests = 8;
    private const int MaxPromptChars = 80;

    public static bool IsUnmapped(string module) => module.StartsWith(UnmappedPrefix, StringComparison.Ordinal);

    // Modules ordered by their latest change, newest first.
    public static IReadOnlyList<SessionModuleChange> ByModule(IReadOnlyList<SessionChangeThread> threads, IReadOnlyDictionary<string, string> moduleByPath)
    {
        return threads
            .SelectMany(t => t.Files.Select(f => (Module: moduleByPath.GetValueOrDefault(f) ?? $"{UnmappedPrefix} thread {Short(t.ThreadId)}", Thread: t, File: f)))
            .GroupBy(x => x.Module, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SessionModuleChange(g.Key,
                g.Select(x => x.Thread).Distinct().OrderByDescending(t => t.At).ToList(),
                g.Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(m => m.LatestAt)
            .ToList();
    }

    public static string Brief(IReadOnlyList<SessionModuleChange> changes, bool canAsk, UnitTestSession? unitTests, DateTime now)
    {
        var builder = new StringBuilder("# Changes in this session (files the earlier AIT threads of this chat changed, per module, newest first)\n");
        foreach (var change in changes)
        {
            var threads = string.Join(", ", change.Threads.Select(t => $"{Short(t.ThreadId)} {Ago(now - t.At)} \"{Clip(OneLine(t.Prompt), MaxPromptChars)}\""));
            builder.Append($"- {change.Module} · thread {threads} · {change.Files.Count} file(s): ")
                .Append(string.Join(", ", change.Files.Take(MaxListedFiles)))
                .Append(change.Files.Count > MaxListedFiles ? $", … {change.Files.Count - MaxListedFiles} more" : string.Empty);
            if (unitTests is not null)
            {
                var modules = IsUnmapped(change.Module) ? Array.Empty<string>() : [change.Module];
                var covering = UnitTestSelection.Relevant(unitTests.Active, Common.Enums.UnitTestMode.Relevant, change.Files, modules)
                    .Select(t => t.DisplayNumber).ToList();
                builder.Append(" · unit tests covering it: ").Append(covering.Count == 0 ? "none"
                    : string.Join(", ", covering.Take(MaxListedTests)) + (covering.Count > MaxListedTests ? $", … ({covering.Count})" : string.Empty));
            }
            builder.Append('\n');
        }

        builder.Append("When the request refers to an earlier change without naming it (\"this change\", \"the change earlier\", \"test it\", \"review what you did\", \"undo that\"), it means files listed here — never the whole git diff: another session may be changing the same working tree.");
        if (changes.Count > 1)
        {
            builder.Append(canAsk
                ? $" More than one module is listed: unless the request names a module, file or feature that tells which, call ask_user before anything else — one option per module (the newest {MaxAskedModules} at most, newest first, each with its thread and file count; name a \"{UnmappedPrefix}\" entry after its files' feature) and one last option for all of them; the developer may also name another one. Use what they choose."
                : " More than one module is listed and you cannot ask: unless the request tells which, take the newest module and say so in the summary.");
        }
        return builder.Append('\n').ToString();
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "(just now)"
        : span.TotalHours < 1 ? $"({(int)span.TotalMinutes} min ago)"
        : span.TotalDays < 1 ? $"({(int)span.TotalHours} h ago)"
        : $"({(int)span.TotalDays} d ago)";
}

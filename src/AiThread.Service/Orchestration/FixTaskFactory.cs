using System.Text;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Fix tasks for one verification round, plus the problems no executor can fix (with the reason).
public sealed record FixPlan(IReadOnlyList<PlanTask> Tasks, IReadOnlyList<string> Unfixable);

// Turns build errors and review issues into executor tasks: one task per file, ids "V<round>-<n>". Build errors are
// only fixed in files this thread changed (an error elsewhere may be older than the thread; the reviewer can still
// name such a file when the change clearly caused it). Generated folders, files outside the root and files that do
// not exist are never targeted.
public static class FixTaskFactory
{
    private const int MaxErrorsPerFile = 25;
    private const int MaxOriginalInstructionChars = 1500;

    public static FixPlan Create(
        int round,
        BuildResult? build,
        ReviewResult? review,
        IReadOnlyCollection<string> changedFiles,
        IReadOnlyList<PlanTask> earlierTasks,
        WorkspaceScope scope)
    {
        var work = new SortedDictionary<string, FileWork>(StringComparer.OrdinalIgnoreCase);
        var unfixable = new List<string>();
        var changed = new HashSet<string>(changedFiles, StringComparer.OrdinalIgnoreCase);

        if (build is { Passed: false, TimedOut: false, StartError: null })
        {
            foreach (var error in build.Errors.Where(e => !e.IsLockedOutput))
            {
                var line = BuildReport.Line(error, scope);
                if (error.File is null || !scope.IsInside(error.File))
                {
                    unfixable.Add($"{line} — not in a project file");
                    continue;
                }

                var relative = scope.ToRelative(error.File);
                if (scope.IsIgnoredPath(relative)) unfixable.Add($"{line} — generated or third-party file");
                else if (!changed.Contains(relative)) unfixable.Add($"{line} — in a file this thread did not change");
                else if (!File.Exists(error.File)) unfixable.Add($"{line} — file not found");
                else Work(work, relative, error.File).Errors.Add(error);
            }

            if (build.Errors.Count == 0) unfixable.Add("The build failed without a recognisable error message (see the output).");
        }

        foreach (var issue in review?.Issues ?? [])
        {
            var label = $"{issue.File}: {issue.Problem}";
            if (!scope.TryResolve(issue.File, out var fullPath, out _)) unfixable.Add($"{label} — outside the project root");
            else if (scope.IsIgnoredPath(scope.ToRelative(fullPath))) unfixable.Add($"{label} — generated or third-party file");
            else if (!File.Exists(fullPath)) unfixable.Add($"{label} — file does not exist");
            else Work(work, scope.ToRelative(fullPath), fullPath).Issues.Add(issue);
        }

        var tasks = new List<PlanTask>();
        foreach (var (relative, fileWork) in work)
        {
            var original = earlierTasks.Where(t => string.Equals(t.File, relative, StringComparison.OrdinalIgnoreCase)).ToList();
            tasks.Add(new PlanTask
            {
                Id = $"V{round}-{tasks.Count + 1}",
                Title = fileWork.Errors.Count > 0 && fileWork.Issues.Count > 0 ? "Fix build errors and review findings"
                    : fileWork.Errors.Count > 0 ? "Fix build errors"
                    : "Fix review findings",
                File = relative,
                Action = "edit",
                Instruction = Instruction(fileWork, original),
                Focus = Focus(fileWork),
                Context = original.SelectMany(t => t.Context ?? [])
                    .DistinctBy(c => (c.File.ToLowerInvariant(), c.StartLine, c.EndLine))
                    .ToList()
            });
        }

        return new FixPlan(tasks, unfixable);
    }

    private static FileWork Work(SortedDictionary<string, FileWork> work, string relative, string fullPath)
    {
        if (!work.TryGetValue(relative, out var fileWork)) work[relative] = fileWork = new FileWork(fullPath);
        return fileWork;
    }

    private static string Instruction(FileWork work, IReadOnlyList<PlanTask> original)
    {
        var builder = new StringBuilder();
        builder.Append("Verification after the previous changes found problems in this file. Fix exactly these problems and change nothing else; keep every name from the contract.\n");

        if (work.Errors.Count > 0)
        {
            builder.Append("\nBuild errors:\n");
            foreach (var error in work.Errors.Take(MaxErrorsPerFile))
            {
                var location = error.Line is { } line ? (error.Column is { } column ? $"line {line}, column {column}" : $"line {line}") : "file";
                builder.Append($"- {location}: {(error.Code is null ? string.Empty : error.Code + ": ")}{error.Message}\n");
            }
            if (work.Errors.Count > MaxErrorsPerFile) builder.Append($"- ... {work.Errors.Count - MaxErrorsPerFile} more errors of the same build\n");
        }

        if (work.Issues.Count > 0)
        {
            builder.Append("\nReviewer findings:\n");
            foreach (var issue in work.Issues)
            {
                builder.Append($"- Problem: {issue.Problem}\n  Fix: {issue.Fix}\n");
                if (issue.Anchor is not null) builder.Append($"  Near: {issue.Anchor}\n");
            }
        }

        foreach (var task in original)
        {
            var instruction = task.Instruction.Trim();
            if (instruction.Length > MaxOriginalInstructionChars) instruction = instruction[..MaxOriginalInstructionChars] + " …";
            builder.Append($"\nEarlier instruction for this file ({task.Id}, already applied — for context):\n").Append(instruction).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    // The top of the file (imports, usings) plus the lines around every error and review anchor; only matters for
    // files too large to send whole.
    private static List<LineRange> Focus(FileWork work)
    {
        var ranges = new List<LineRange> { new() { StartLine = 1, EndLine = 1 } };
        ranges.AddRange(work.Errors.Where(e => e.Line is > 0).Select(e => new LineRange { StartLine = e.Line!.Value, EndLine = e.Line.Value }));

        var anchors = work.Issues.Select(i => i.Anchor).OfType<string>().Select(a => a.Trim()).Where(a => a.Length >= 3).ToList();
        if (anchors.Count > 0)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(work.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines = [];
            }

            foreach (var anchor in anchors)
            {
                var index = Array.FindIndex(lines, l => l.Trim() == anchor);
                if (index < 0) index = Array.FindIndex(lines, l => l.Contains(anchor, StringComparison.Ordinal));
                if (index >= 0) ranges.Add(new LineRange { StartLine = index + 1, EndLine = index + 1 });
            }
        }

        return ranges.DistinctBy(r => r.StartLine).OrderBy(r => r.StartLine).ToList();
    }

    private sealed class FileWork(string fullPath)
    {
        public string FullPath { get; } = fullPath;
        public List<BuildDiagnostic> Errors { get; } = [];
        public List<ReviewIssue> Issues { get; } = [];
    }
}

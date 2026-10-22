using System.Text;
using System.Text.RegularExpressions;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

// Fix tasks for one verification round, plus the problems no executor can fix (with the reason).
public sealed record FixPlan(IReadOnlyList<PlanTask> Tasks, IReadOnlyList<string> Unfixable);

// Turns build errors and review issues into executor tasks: one task per file, ids "V<round>-<n>". Build errors are
// only fixed in files this thread changed (an error elsewhere may be older than the thread; the reviewer can still
// name such a file when the change clearly caused it). Generated folders, files outside the root and files that do
// not exist are never targeted.
//
// Duplicate C# members (CS0111, CS0102) between a file this thread filled with members (create or extract target) and a
// file that held them before mean a move that was not finished. They become extract tasks from the old file, done
// without a model, so the copy the thread meant to keep stays; an executor told "remove the duplicate" in each new file
// would undo the move instead.
//
// A missing name (CS0103, CS0117, CS1061) that the plan told another task to declare, in a file that still does not
// contain it, is fixed in that file: the executor of the calling file can only report that the call matches the plan.
//
// Unit tests that fail after the change (testIssues, from UnitTestStage) are handled like review findings: one task per
// file, the code unless the issue says the test of this change is wrong.
public static partial class FixTaskFactory
{
    private const int MaxErrorsPerFile = 25;
    private const int MaxOriginalInstructionChars = 1500;
    private const int MaxExamplesPerName = 3;
    private const int MaxExamplesPerFile = 6;
    private const int CallSiteMargin = 5;

    private static readonly HashSet<string> CommonNames = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "bool", "object", "void", "var", "decimal", "double", "float", "null", "System", "Task"
    };

    public static FixPlan Create(
        int round,
        BuildResult? build,
        ReviewResult? review,
        IReadOnlyCollection<string> changedFiles,
        IReadOnlyList<PlanTask> earlierTasks,
        WorkspaceScope scope,
        IReadOnlyList<ReviewIssue>? testIssues = null)
    {
        var work = new SortedDictionary<string, FileWork>(StringComparer.OrdinalIgnoreCase);
        var unfixable = new List<string>();
        var changed = new HashSet<string>(changedFiles, StringComparer.OrdinalIgnoreCase);
        var moves = new SortedDictionary<(string From, string To), SortedSet<string>>(PairComparer.Instance);

        if (build is { Passed: false, TimedOut: false, StartError: null })
        {
            var duplicates = UnfinishedMoves(build.Errors, changed, earlierTasks, scope, moves);
            foreach (var error in build.Errors.Where(e => !e.IsLockedOutput && !duplicates.Contains(e)))
            {
                var line = BuildReport.Line(error, scope);
                if (error.File is null || !scope.IsInside(error.File))
                {
                    unfixable.Add($"{line} — not in a project file");
                    continue;
                }

                var relative = scope.ToRelative(error.File);
                if (scope.IsIgnoredPath(relative)) unfixable.Add($"{line} — generated or third-party file");
                else if (MissingDeclaration(error, relative, earlierTasks, scope) is { } owner)
                    Work(work, owner.Relative, owner.FullPath).Missing.Add(new MissingName(owner.Name, relative, error));
                else if (!changed.Contains(relative)) unfixable.Add($"{line} — in a file this thread did not change");
                else if (!File.Exists(error.File)) unfixable.Add($"{line} — file not found");
                else Work(work, relative, error.File).Errors.Add(error);
            }

            if (build.Errors.Count == 0) unfixable.Add("The build failed without a recognisable error message (see the output).");
        }

        // A finding on a file the unfinished moves change is usually about those very duplicates; the next round reviews
        // the files again after the move, so the finding is not handed to an executor that would delete code twice.
        var moving = new HashSet<string>(moves.Keys.SelectMany(k => new[] { k.From, k.To }), StringComparer.OrdinalIgnoreCase);
        foreach (var issue in review?.Issues ?? [])
        {
            var label = $"{issue.File}: {issue.Problem}";
            if (!scope.TryResolve(issue.File, out var fullPath, out _)) unfixable.Add($"{label} — outside the project root");
            else if (scope.IsIgnoredPath(scope.ToRelative(fullPath))) unfixable.Add($"{label} — generated or third-party file");
            else if (!File.Exists(fullPath)) unfixable.Add($"{label} — file does not exist");
            else if (!moving.Contains(scope.ToRelative(fullPath))) Work(work, scope.ToRelative(fullPath), fullPath).Issues.Add(issue);
        }

        foreach (var issue in testIssues ?? [])
        {
            var label = $"{issue.File}: {issue.Problem}";
            if (!scope.TryResolve(issue.File, out var fullPath, out _)) unfixable.Add($"{label} — outside the project root");
            else if (scope.IsIgnoredPath(scope.ToRelative(fullPath))) unfixable.Add($"{label} — generated or third-party file");
            else if (!File.Exists(fullPath)) unfixable.Add($"{label} — file does not exist");
            else Work(work, scope.ToRelative(fullPath), fullPath).TestIssues.Add(issue);
        }

        var tasks = new List<PlanTask>();
        foreach (var ((from, to), members) in moves)
        {
            var names = string.Join(", ", members);
            tasks.Add(new PlanTask
            {
                Id = $"V{round}-{tasks.Count + 1}",
                Title = "Finish moving duplicated members",
                File = from,
                Action = PlanTask.Extract,
                To = to,
                Members = members.ToList(),
                Instruction = $"Move {names} from {from} to {to}. The build reports them twice (CS0111/CS0102): this thread already put them in {to}, " +
                              $"so AIT removes them from {from}; members {to} already declares are only removed."
            });
        }

        var extracts = tasks.ToList();
        foreach (var (relative, fileWork) in work)
        {
            var original = earlierTasks.Where(t => string.Equals(t.IsMove ? t.To : t.File, relative, StringComparison.OrdinalIgnoreCase)
                                                   || (t.IsExtract && string.Equals(t.To, relative, StringComparison.OrdinalIgnoreCase))).ToList();
            var after = extracts.Where(t => string.Equals(t.File, relative, StringComparison.OrdinalIgnoreCase) || string.Equals(t.To, relative, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id).ToList();
            var hasErrors = fileWork.Errors.Count > 0 || fileWork.Missing.Count > 0;
            var examples = Examples(fileWork);
            tasks.Add(new PlanTask
            {
                Id = $"V{round}-{tasks.Count + 1}",
                DependsOn = after.Count > 0 ? after : null,
                Title = hasErrors && fileWork.Issues.Count > 0 ? "Fix build errors and review findings"
                    : hasErrors ? "Fix build errors"
                    : fileWork.Issues.Count > 0 ? "Fix review findings"
                    : "Fix failing unit tests",
                File = relative,
                Action = "edit",
                Instruction = Instruction(fileWork, original, examples),
                Focus = Focus(fileWork, examples),
                Context = original.SelectMany(t => t.Context ?? [])
                    .Concat(CallSites(fileWork))
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

    private static string Instruction(FileWork work, IReadOnlyList<PlanTask> original, IReadOnlyList<(int Line, string Text)> examples)
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

        if (work.Missing.Count > 0)
        {
            builder.Append("\nThe build reports names that this file was meant to declare as missing where other files use them. " +
                           "Declare them in this file as the earlier instruction describes, with the signature the calling code expects " +
                           "(the call sites are in the reference excerpts):\n");
            foreach (var missing in work.Missing.Take(MaxErrorsPerFile))
            {
                var at = missing.Error.Line is { } line ? $" line {line}" : string.Empty;
                builder.Append($"- {missing.Name} — used in {missing.UsedIn}{at}: {(missing.Error.Code is null ? string.Empty : missing.Error.Code + ": ")}{missing.Error.Message}\n");
            }
        }

        if (examples.Count > 0)
        {
            builder.Append("\nOther lines of this file that use the same names — follow their pattern (they are in the excerpts):\n");
            foreach (var (line, text) in examples) builder.Append($"- line {line}: {text}\n");
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

        if (work.TestIssues.Count > 0)
        {
            builder.Append("\nUnit tests that fail after the change (fix this file so they pass; change a test only when the fix says so):\n");
            foreach (var issue in work.TestIssues)
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

    // The top of the file (imports, usings) plus the lines around every error, review anchor and usage example, and the
    // end of the file when a missing declaration has to go in; only matters for files too large to send whole.
    private static List<LineRange> Focus(FileWork work, IReadOnlyList<(int Line, string Text)> examples)
    {
        var ranges = new List<LineRange> { new() { StartLine = 1, EndLine = 1 } };
        ranges.AddRange(work.Errors.Where(e => e.Line is > 0).Select(e => new LineRange { StartLine = e.Line!.Value, EndLine = e.Line.Value }));
        ranges.AddRange(examples.Select(e => new LineRange { StartLine = e.Line, EndLine = e.Line }));

        var lines = work.Lines;
        if (work.Missing.Count > 0 && lines.Length > 0) ranges.Add(new LineRange { StartLine = lines.Length, EndLine = lines.Length });

        foreach (var anchor in work.Issues.Concat(work.TestIssues).Select(i => i.Anchor).OfType<string>().Select(a => a.Trim()).Where(a => a.Length >= 3))
        {
            var index = Array.FindIndex(lines, l => l.Trim() == anchor);
            if (index < 0) index = Array.FindIndex(lines, l => l.Contains(anchor, StringComparison.Ordinal));
            if (index >= 0) ranges.Add(new LineRange { StartLine = index + 1, EndLine = index + 1 });
        }

        return ranges.DistinctBy(r => r.StartLine).OrderBy(r => r.StartLine).ToList();
    }

    // Other lines of the file that use a name from an error message: how every other method constructs 'OracleEntities'
    // when one call misses its argument (CS7036), for example. A large file reaches the executor as excerpts around the
    // errors only, so without these it cannot see the pattern to follow.
    private static List<(int Line, string Text)> Examples(FileWork work)
    {
        var lines = work.Lines;
        var found = new List<(int Line, string Text)>();
        if (lines.Length == 0) return found;

        var errorLines = work.Errors.Where(e => e.Line is > 0).Select(e => e.Line!.Value).ToList();
        foreach (var name in work.Errors.SelectMany(e => QuotedNames(e.Message)).Distinct(StringComparer.Ordinal))
        {
            var hits = 0;
            for (var i = 0; i < lines.Length && hits < MaxExamplesPerName && found.Count < MaxExamplesPerFile; i++)
            {
                var number = i + 1;
                if (errorLines.Any(l => Math.Abs(l - number) <= 2) || !ContainsWord(lines[i], name)) continue;
                var text = lines[i].Trim();
                if (text.Length == 0 || found.Any(f => f.Text == text)) continue;
                found.Add((number, text));
                hits++;
            }
        }
        return found.OrderBy(f => f.Line).ToList();
    }

    // Simple names quoted in a compiler message: 'OracleEntities.OracleEntities(string)' gives OracleEntities.
    private static IEnumerable<string> QuotedNames(string message)
    {
        foreach (Match match in QuotedText().Matches(message))
        {
            var text = match.Groups[1].Value;
            var paren = text.IndexOf('(');
            if (paren >= 0) text = text[..paren];
            var generic = text.IndexOf('<');
            if (generic >= 0) text = text[..generic];
            var name = text[(text.LastIndexOf('.') + 1)..];
            if (name.Length >= 3 && Identifier().IsMatch(name) && !CommonNames.Contains(name)) yield return name;
        }
    }

    // The calling lines of every missing declaration, so the executor of the declaring file sees the expected signature.
    private static IEnumerable<ContextRef> CallSites(FileWork work) =>
        work.Missing
            .Where(m => m.Error.Line is > 0)
            .Select(m => new ContextRef
            {
                File = m.UsedIn,
                StartLine = Math.Max(1, m.Error.Line!.Value - CallSiteMargin),
                EndLine = m.Error.Line.Value + CallSiteMargin,
                Note = $"uses {m.Name}"
            });

    // CS0103: The name 'X' does not exist in the current context
    // CS0117: 'T' does not contain a definition for 'X'
    // CS1061: 'T' does not contain a definition for 'X' and no accessible extension method 'X' ...
    [GeneratedRegex("(?:The name '(?<name>[^']+)' does not exist|does not contain a definition for '(?<name>[^']+)')")]
    private static partial Regex MissingNameMessage();

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex QuotedText();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    // The one other C# file whose task instruction names the missing name while the file itself still does not contain
    // it. Several such files (or none) leave the error with the calling file.
    private static (string Name, string Relative, string FullPath)? MissingDeclaration(
        BuildDiagnostic error, string errorFile, IReadOnlyList<PlanTask> earlierTasks, WorkspaceScope scope)
    {
        if (error.Code is not ("CS0103" or "CS0117" or "CS1061")) return null;
        var match = MissingNameMessage().Match(error.Message);
        if (!match.Success) return null;
        var name = match.Groups["name"].Value;
        if (name.Length < 3 || !Identifier().IsMatch(name)) return null;

        var owners = new List<(string Relative, string FullPath)>();
        foreach (var file in earlierTasks
                     .Where(t => (t.Action == PlanTask.Edit || t.IsCreate) && ContainsWord(t.Instruction, name))
                     .Select(t => t.File)
                     .Where(f => IsCSharp(f) && !string.Equals(f, errorFile, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!scope.TryResolve(file, out var fullPath, out _) || scope.IsIgnoredPath(file) || !File.Exists(fullPath)) continue;
            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!ContainsWord(content, name)) owners.Add((scope.ToRelative(fullPath), fullPath));
        }

        return owners.Count == 1 ? (name, owners[0].Relative, owners[0].FullPath) : null;
    }

    private static bool ContainsWord(string text, string word)
    {
        for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.Ordinal))
        {
            var before = i == 0 ? ' ' : text[i - 1];
            var after = i + word.Length >= text.Length ? ' ' : text[i + word.Length];
            if (!IsIdentifierChar(before) && !IsIdentifierChar(after)) return true;
        }
        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // CS0111: Type 'T' already defines a member called 'M' with the same parameter types
    // CS0102: The type 'T' already contains a definition for 'M'
    [GeneratedRegex("'(?<type>[^']+)'[^']*'(?<member>[^']+)'")]
    private static partial Regex DuplicateMemberMessage();

    // Fills `moves` (old file → file the thread filled → member names) and returns the errors they resolve.
    private static HashSet<BuildDiagnostic> UnfinishedMoves(
        IReadOnlyList<BuildDiagnostic> errors,
        HashSet<string> changed,
        IReadOnlyList<PlanTask> earlierTasks,
        WorkspaceScope scope,
        SortedDictionary<(string From, string To), SortedSet<string>> moves)
    {
        var handled = new HashSet<BuildDiagnostic>();
        var candidates = errors.Where(e => e.Code is "CS0111" or "CS0102" && e.File is not null && scope.IsInside(e.File)).ToList();
        if (candidates.Count == 0) return handled;

        var destinations = earlierTasks.Where(t => t.IsCreate).Select(t => t.File)
            .Concat(earlierTasks.Where(t => t.IsExtract && t.To is not null).Select(t => t.To!))
            .Where(f => changed.Contains(f) && IsCSharp(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var origins = earlierTasks.Where(t => t.Action == PlanTask.Edit || t.IsExtract).Select(t => t.File)
            .Where(f => IsCSharp(f) && !destinations.Contains(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (destinations.Count == 0 || origins.Count == 0) return handled;

        var contents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        bool Declares(string relative, string type, string member)
        {
            if (!contents.TryGetValue(relative, out var content))
            {
                try
                {
                    content = scope.TryResolve(relative, out var fullPath, out _) && File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    content = null;
                }
                contents[relative] = content;
            }
            return content is not null && CSharpMemberMover.Declares(content, type, member);
        }

        foreach (var error in candidates)
        {
            var match = DuplicateMemberMessage().Match(error.Message);
            if (!match.Success) continue;
            var type = match.Groups["type"].Value;
            var member = CSharpMemberMover.NormalizeName(match.Groups["member"].Value);
            if (member.Length == 0 || member.StartsWith('.') || member == "this") continue;

            var relative = scope.ToRelative(error.File!);
            string? from = null, to = null;
            if (destinations.Contains(relative))
            {
                to = relative;
                from = origins.FirstOrDefault(o => Declares(o, type, member));
            }
            else if (origins.Contains(relative))
            {
                from = relative;
                to = destinations.FirstOrDefault(d => Declares(d, type, member));
            }
            if (from is null || to is null) continue;

            if (!moves.TryGetValue((from, to), out var members)) moves[(from, to)] = members = new SortedSet<string>(StringComparer.Ordinal);
            members.Add(member);
            handled.Add(error);
        }

        return handled;
    }

    private static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private sealed class PairComparer : IComparer<(string From, string To)>
    {
        public static readonly PairComparer Instance = new();

        public int Compare((string From, string To) x, (string From, string To) y)
        {
            var from = StringComparer.OrdinalIgnoreCase.Compare(x.From, y.From);
            return from != 0 ? from : StringComparer.OrdinalIgnoreCase.Compare(x.To, y.To);
        }
    }

    private sealed record MissingName(string Name, string UsedIn, BuildDiagnostic Error);

    private sealed class FileWork(string fullPath)
    {
        private string[]? _lines;

        public string FullPath { get; } = fullPath;
        public List<BuildDiagnostic> Errors { get; } = [];
        public List<ReviewIssue> Issues { get; } = [];
        public List<ReviewIssue> TestIssues { get; } = [];
        public List<MissingName> Missing { get; } = [];

        public string[] Lines
        {
            get
            {
                if (_lines is not null) return _lines;
                try
                {
                    _lines = File.ReadAllLines(FullPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _lines = [];
                }
                return _lines;
            }
        }
    }
}

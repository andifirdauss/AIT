using AIThread.Common.Enums;
using AIThread.Service.Approvals;

namespace AIThread.Service.Workspace.Commands;

public enum CommandVerdict
{
    // Only reads: runs without asking.
    ReadOnly = 1,

    // May change something, reach another host or do what AIT cannot check: runs after the developer allows Items.
    NeedsApproval = 2,

    // Never runs (reads a credential file, an empty command, ...).
    Refused = 3
}

public sealed record CommandCheck(CommandVerdict Verdict, string? Reason = null, IReadOnlyList<ApprovalItem>? Items = null)
{
    public static readonly CommandCheck ReadOnly = new(CommandVerdict.ReadOnly);
}

// Decides how a run_command line may run, without a shell. A plain pipeline whose every command is on the read-only
// allowlist runs at once (curl GET/HEAD to this machine, git reads, listing and reading files, version checks); a curl
// to another host needs that host allowed; anything else needs the exact command allowed. A word that names a
// credential file (SecretPaths and the project's rules) refuses the command whatever it is.
public static partial class CommandPolicy
{
    public const int MaxCommandChars = 4_000;

    private const string RecursiveSearch = "searches folders recursively (use search_text, which skips credential files)";

    // Commands that only print what they are given or what a file holds; their file arguments are checked for secrets.
    private static readonly HashSet<string> Readers = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat", "type", "head", "tail", "wc", "sort", "uniq", "cut", "tr", "jq", "file", "stat", "du", "df", "basename",
        "dirname", "realpath", "readlink", "nl", "column", "xxd", "od", "md5sum", "sha1sum", "sha256sum", "base64"
    };

    // Commands that print facts about the machine.
    private static readonly HashSet<string> Listers = new(StringComparer.OrdinalIgnoreCase)
    {
        "pwd", "echo", "which", "where", "whereis", "whoami", "hostname", "date", "uname", "nslookup", "ping", "true", "false"
    };

    // Commands that list file names; a name pattern only picks names, so it may stay unquoted.
    private static readonly HashSet<string> NameListers = new(StringComparer.OrdinalIgnoreCase) { "ls", "dir", "tree", "find" };

    private static readonly HashSet<string> FindActions = new(StringComparer.Ordinal)
    {
        "-exec", "-execdir", "-ok", "-okdir", "-delete", "-fprint", "-fprint0", "-fprintf", "-fls"
    };

    // Tools whose version or listing subcommands only read.
    private static readonly Dictionary<string, string[]> InfoCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = ["--info", "--version", "--list-sdks", "--list-runtimes"],
        ["node"] = ["-v", "--version"],
        ["npm"] = ["-v", "--version", "ls", "list", "view", "info", "outdated", "explain", "why"],
        ["yarn"] = ["-v", "--version", "list", "info", "why"],
        ["pnpm"] = ["-v", "--version", "ls", "list", "why", "outdated"],
        ["php"] = ["-v", "--version", "-m"],
        ["composer"] = ["--version", "show", "outdated", "depends", "why"],
        ["python"] = ["--version", "-V"],
        ["python3"] = ["--version", "-V"],
        ["py"] = ["--version", "-V"],
        ["pip"] = ["--version", "list", "show", "freeze"],
        ["pip3"] = ["--version", "list", "show", "freeze"],
        ["java"] = ["-version", "--version"],
        ["javac"] = ["-version", "--version"],
        ["mvn"] = ["-v", "--version"],
        ["gradle"] = ["-v", "--version"],
        ["go"] = ["version", "list"],
        ["cargo"] = ["--version", "tree", "metadata"],
        ["rustc"] = ["--version"],
        ["docker"] = ["version", "ps", "images", "logs"],
        ["kubectl"] = ["version", "get", "describe", "logs"]
    };

    public static CommandCheck Classify(string? command, WorkspaceScope scope, string workingDirectory)
    {
        var text = command?.Trim() ?? string.Empty;
        if (text.Length == 0) return new CommandCheck(CommandVerdict.Refused, "the command is empty");
        if (text.Length > MaxCommandChars) return new CommandCheck(CommandVerdict.Refused, $"the command is longer than {MaxCommandChars} characters");

        var line = CommandLine.Parse(text);
        // Every word that reaches a program is checked for credential files, also in commands that need approval.
        foreach (var word in line.Segments.SelectMany(s => s))
            if (NamesSecret(word.Text, scope, workingDirectory) is { } secret)
                return new CommandCheck(CommandVerdict.Refused, $"'{secret}' is a protected credential file (AIT secret policy)");

        var whole = Ask(text, line.Operator is { } op ? $"uses the shell operator {op}" : null);
        if (line.Operator is not null) return whole;
        if (line.Segments.Any(s => s.Count == 0)) return whole with { Reason = "has an empty pipeline part" };

        var hosts = new List<ApprovalItem>();
        for (var i = 0; i < line.Segments.Count; i++)
        {
            var reason = CheckSegment(line.Segments[i], i, hosts);
            if (reason is not null) return whole with { Reason = reason };
        }

        return hosts.Count == 0
            ? CommandCheck.ReadOnly
            : new CommandCheck(CommandVerdict.NeedsApproval, "calls a host outside this machine",
                hosts.DistinctBy(h => h.Target, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static CommandCheck Ask(string command, string? reason) =>
        new(CommandVerdict.NeedsApproval, reason,
            [new ApprovalItem(ApprovalKind.Command, ApprovalTargets.Normalize(ApprovalKind.Command, command), Pattern: ApprovalTargets.SuggestPattern(command))]);

    // Null when the command of one pipeline part only reads; otherwise why it needs approval. Position 0 is the first
    // command; the others read the previous command's output.
    private static string? CheckSegment(IReadOnlyList<CommandWord> words, int position, List<ApprovalItem> hosts)
    {
        if (words.Any(w => w.Expands)) return "expands a variable or a command substitution";
        var program = ProgramName(words[0].Text);
        var args = words.Skip(1).ToList();

        if (program == "curl") return CheckCurl(args, hosts);
        if (program == "git") return CheckGit(args);
        if (NameListers.Contains(program))
            return program == "find" && args.Any(a => FindActions.Contains(a.Text)) ? "find runs an action (-exec, -delete, ...)" : null;
        if (args.Any(a => a.Glob)) return "uses a file name pattern the shell expands";
        if (Listers.Contains(program) || Readers.Contains(program)) return null;
        if (program is "grep" or "egrep" or "fgrep" or "findstr")
            return args.Any(a => IsRecursiveSearch(program, a.Text)) ? RecursiveSearch : null;
        if (program == "rg") return position > 0 ? null : RecursiveSearch;
        if (InfoCommands.TryGetValue(program, out var allowed))
            return args.Count > 0 && allowed.Contains(args[0].Text, StringComparer.OrdinalIgnoreCase)
                ? null
                : $"'{program}' only reads with {string.Join(", ", allowed)}";
        return $"'{program}' is not on the read-only list";
    }

    private static bool IsRecursiveSearch(string program, string arg)
    {
        if (program == "findstr") return arg.Equals("/s", StringComparison.OrdinalIgnoreCase);
        if (arg is "--recursive" or "--dereference-recursive" || arg.StartsWith("--directories", StringComparison.Ordinal)) return true;
        return arg.Length > 1 && arg[0] == '-' && arg[1] != '-' && (arg.Contains('r') || arg.Contains('R'));
    }

    // "C:\Git\usr\bin\curl.exe" → "curl".
    public static string ProgramName(string word)
    {
        var name = word.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].ToLowerInvariant();
        foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            if (name.EndsWith(extension, StringComparison.Ordinal)) return name[..^extension.Length];
        return name;
    }

    // The credential file a word points to, as the developer wrote it; null for any other word. Covers "@file" (curl
    // data), "--opt=file", "rev:path" (git show) and "~/".
    private static string? NamesSecret(string word, WorkspaceScope scope, string workingDirectory)
    {
        if (word.Length == 0 || word.Contains("://", StringComparison.Ordinal)) return null;
        foreach (var candidate in Candidates(word))
        {
            if (candidate.Length == 0) continue;
            var path = candidate == "~" || candidate.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), candidate.TrimStart('~', '/'))
                : candidate;
            try
            {
                var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path));
                if (scope.IsProtected(full)) return word;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string word)
    {
        yield return word;
        if (word.StartsWith('@')) yield return word[1..];
        if (word.StartsWith('-') && word.IndexOf('=') is var equals and > 0) yield return word[(equals + 1)..].TrimStart('@');
        // git's "<rev>:<path>"; a drive letter ("C:\") is not one.
        if (word.IndexOf(':') is var colon and > 1 && colon < word.Length - 1) yield return word[(colon + 1)..];
    }
}

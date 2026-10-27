namespace AIThread.Service.Workspace.Commands;

// git reads with the subcommands that only look at the repository; listing forms of branch, tag, remote, stash, reflog,
// worktree and config. Configuration on the command line (-c) and output files (--output) are never read-only.
public static partial class CommandPolicy
{
    private static readonly HashSet<string> GitReads = new(StringComparer.Ordinal)
    {
        "status", "log", "diff", "show", "blame", "annotate", "rev-parse", "ls-files", "ls-tree", "ls-remote", "cat-file",
        "describe", "shortlog", "rev-list", "merge-base", "name-rev", "for-each-ref", "show-ref", "version", "help",
        "whatchanged", "count-objects", "check-ignore", "diff-tree", "diff-files", "diff-index", "range-diff", "cherry"
    };

    private static readonly HashSet<string> GitBranchListing = new(StringComparer.Ordinal)
    {
        "-a", "--all", "-r", "--remotes", "-v", "-vv", "--verbose", "-l", "--list", "--show-current", "--merged", "--no-merged",
        "--contains", "--no-contains", "--points-at", "--sort", "--format", "--column", "--no-column", "-i", "--ignore-case"
    };

    private static readonly HashSet<string> GitTagListing = new(StringComparer.Ordinal)
    {
        "-l", "--list", "-n", "--contains", "--no-contains", "--points-at", "--merged", "--no-merged", "--sort", "--format",
        "--column", "--no-column", "-i", "--ignore-case"
    };

    private static readonly HashSet<string> GitConfigReads = new(StringComparer.Ordinal)
    {
        "--get", "--get-all", "--get-regexp", "--list", "-l", "--show-origin", "--show-scope", "--name-only", "--local",
        "--global", "--system", "--worktree", "--null", "-z", "--type", "--default", "get"
    };

    private static string? CheckGit(IReadOnlyList<CommandWord> args)
    {
        var i = 0;
        // Global options before the subcommand.
        for (; i < args.Count && args[i].Text.StartsWith('-'); i++)
        {
            var option = args[i].Text;
            if (option is "--no-pager" or "-P" or "--no-optional-locks" or "--version" or "--help") continue;
            if (option == "-C" && i + 1 < args.Count)
            {
                i++;
                continue;
            }
            return option == "-c" || option.StartsWith("--config-env", StringComparison.Ordinal)
                ? "sets git configuration on the command line"
                : $"uses the git option {option}, which AIT cannot check";
        }
        if (i >= args.Count) return null;

        var command = args[i].Text;
        var rest = args.Skip(i + 1).Select(a => a.Text).ToList();
        if (rest.Any(a => a.StartsWith("--output", StringComparison.Ordinal) || a == "--ext-diff"))
            return "git writes to a file or runs an external diff tool";
        if (GitReads.Contains(command)) return null;

        var reads = command switch
        {
            "branch" => rest.All(a => !a.StartsWith('-') ? rest.Any(o => o is "-l" or "--list" or "--contains" or "--merged" or "--no-merged" or "--points-at") : GitBranchListing.Contains(Option(a))),
            "tag" => rest.All(a => !a.StartsWith('-') ? rest.Any(o => o is "-l" or "--list" or "--contains" or "--points-at") : GitTagListing.Contains(Option(a))),
            "remote" => rest.Count == 0 || rest[0] is "-v" or "--verbose" or "show" or "get-url",
            "stash" => rest.Count > 0 && rest[0] is "list" or "show",
            "reflog" => rest.Count == 0 || rest[0] is "show" || rest[0].StartsWith('-'),
            "worktree" => rest.Count > 0 && rest[0] == "list",
            "notes" => rest.Count > 0 && rest[0] is "list" or "show",
            "config" => rest.Count > 0 && rest.Where(a => a.StartsWith('-') || a == "get").All(a => GitConfigReads.Contains(Option(a)))
                        && rest.Any(a => a is "--get" or "--get-all" or "--get-regexp" or "--list" or "-l" or "get"),
            "symbolic-ref" => rest.Count(a => !a.StartsWith('-')) <= 1 && rest.Where(a => a.StartsWith('-')).All(a => a is "--short" or "-q" or "--quiet"),
            _ => false
        };
        return reads ? null : $"'git {command}' may change the repository";
    }

    // "--sort=-date" → "--sort".
    private static string Option(string arg) => arg.IndexOf('=') is var equals and > 0 ? arg[..equals] : arg;
}

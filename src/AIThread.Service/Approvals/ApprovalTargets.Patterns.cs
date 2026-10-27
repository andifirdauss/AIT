using AIThread.Common.Localization;
using ParsedCommand = AIThread.Service.Workspace.Commands.CommandLine;

namespace AIThread.Service.Approvals;

// Command patterns: a grant "npm install *" covers every plain command whose first words are "npm install" (a single
// command, no pipe, no shell operator, nothing the shell would substitute). A command with operators is only ever
// covered by a grant of the exact same line.
public static partial class ApprovalTargets
{
    public const string PatternSuffix = " *";

    // Programs that delete, move, overwrite, change rights, stop things or run another command line: never a pattern,
    // each command is allowed on its own. curl/wget are governed by host permissions.
    private static readonly HashSet<string> RiskyPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "erase", "rmdir", "rd", "unlink", "shred", "format", "diskpart", "mkfs", "dd", "fdisk", "shutdown", "reboot", "halt",
        "poweroff", "kill", "pkill", "killall", "taskkill", "chmod", "chown", "chgrp", "icacls", "takeown", "attrib", "mv", "move", "ren",
        "rename", "cp", "copy", "xcopy", "robocopy", "truncate", "reg", "regedit", "sc", "net", "netsh", "schtasks", "crontab", "sudo",
        "doas", "runas", "su", "env", "xargs", "nohup", "time", "timeout", "nice", "exec", "eval", "source", "start", "call", "sh", "bash",
        "zsh", "fish", "cmd", "powershell", "pwsh", "wsl", "curl", "wget", "ssh", "scp", "sftp", "rsync", "ftp"
    };

    // Subcommands of any tool that publish, delete or log in somewhere ("npm publish", "docker rm", "gh auth login").
    private static readonly HashSet<string> RiskySubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "push", "publish", "unpublish", "deploy", "release", "delete", "destroy", "drop", "rm", "rmi", "remove", "uninstall", "prune",
        "purge", "wipe", "clean", "reset", "kill", "stop", "login", "logout", "adduser", "token", "auth", "owner", "access", "deprecate"
    };

    // git subcommands that change the repository, the branch or the working tree.
    private static readonly HashSet<string> RiskyGit = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "am", "apply", "branch", "checkout", "cherry-pick", "clean", "commit", "config", "fetch", "filter-branch", "filter-repo",
        "gc", "init", "merge", "mv", "notes", "prune", "pull", "push", "rebase", "reflog", "remote", "replace", "reset", "restore",
        "revert", "rm", "stash", "submodule", "switch", "tag", "update-index", "update-ref", "worktree"
    };

    public static bool IsPattern(string target) => target.TrimEnd().EndsWith(PatternSuffix, StringComparison.Ordinal);

    // The pattern "allow always for the pattern" offers for a command that needs permission: the program and its first
    // word plus " *" ("npm install axios" → "npm install *"). Null when the command is not a single plain command, has
    // fewer than two words, starts with an option, or deletes, publishes or changes the repository.
    public static string? SuggestPattern(string command)
    {
        var line = ParsedCommand.Parse(command);
        if (line.Operator is not null || line.Segments.Count != 1) return null;
        var words = line.Segments[0];
        if (words.Count < 2 || words.Any(w => w.Expands)) return null;
        var pattern = $"{words[0].Text} {words[1].Text}{PatternSuffix}";
        return words[0].Quoted || words[1].Quoted || words[1].Text.Contains("{{", StringComparison.Ordinal) || PatternError(pattern) is not null
            ? null
            : pattern;
    }

    // Why a pattern cannot be a standing permission (for a pattern added by hand), or null when it can.
    public static string? PatternError(string pattern)
    {
        var text = SpacesRegex().Replace(pattern.Trim(), " ");
        if (!IsPattern(text)) return Lang.T("Pola harus diakhiri \" *\", mis. \"npm install *\".", "A pattern ends with \" *\", e.g. \"npm install *\".");
        var line = ParsedCommand.Parse(text[..^PatternSuffix.Length]);
        if (line.Operator is not null || line.Segments.Count != 1)
            return Lang.T("Pola hanya untuk satu perintah, tanpa pipe atau operator shell.", "A pattern is for a single command, without a pipe or shell operator.");
        var words = line.Segments[0];
        if (words.Count < 2)
            return Lang.T("Pola terlalu luas: tulis nama program dan minimal satu kata, mis. \"npm install *\".",
                "The pattern is too broad: write the program and at least one word, e.g. \"npm install *\".");
        if (words.Any(w => w.Glob || w.Expands))
            return Lang.T("Hanya \" *\" di akhir yang boleh menjadi wildcard.", "Only the trailing \" *\" may be a wildcard.");
        if (words[1].Text.StartsWith('-'))
            return Lang.T("Kata kedua harus subperintah, bukan opsi (mis. \"dotnet ef *\").", "The second word must be a subcommand, not an option (e.g. \"dotnet ef *\").");

        var program = ProgramName(words[0].Text);
        var risky = RiskyPrograms.Contains(program)
                    || words.Skip(1).Any(w => RiskySubcommands.Contains(w.Text))
                    || program.Equals("git", StringComparison.OrdinalIgnoreCase) && words.Skip(1).Any(w => RiskyGit.Contains(w.Text));
        return risky
            ? Lang.T($"\"{program} {words[1].Text}\" bisa menghapus, memublikasikan, atau mengubah repository; izinkan perintahnya satu per satu.",
                $"\"{program} {words[1].Text}\" can delete, publish or change the repository; allow its commands one at a time.")
            : null;
    }

    // A pattern grant covers a single plain command that starts with the pattern's words (compared exactly).
    private static bool PatternCovers(string granted, string target)
    {
        if (!IsPattern(granted) || PatternError(granted) is not null) return false;
        var prefix = ParsedCommand.Parse(granted[..^PatternSuffix.Length]).Segments[0];
        var line = ParsedCommand.Parse(target);
        if (line.Operator is not null || line.Segments.Count != 1) return false;
        var words = line.Segments[0];
        if (words.Count < prefix.Count || words.Any(w => w.Expands)) return false;
        for (var i = 0; i < prefix.Count; i++)
            if (!string.Equals(words[i].Text, prefix[i].Text, StringComparison.Ordinal))
                return false;
        return true;
    }

    // "C:\Program Files\nodejs\npm.cmd" → "npm".
    private static string ProgramName(string word)
    {
        var name = word.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 && name[(dot + 1)..].ToLowerInvariant() is "exe" or "cmd" or "bat" or "ps1" or "com" ? name[..dot] : name;
    }
}

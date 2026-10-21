// Planner tools git_status / git_diff / git_log / git_show / git_blame: read-only views of the git repository the
// project lives in. git runs as a process with fixed arguments (no shell): revisions are checked and paths go after
// "--", so the model can never pass an option. Optional locks, pager, colours, external diff and textconv tools and
// signature checks are off, so git neither changes the repository nor waits for input.
// Paths resolve through WorkspaceScope like the other read tools: inside the root git runs in the root (paths in the
// output stay relative to it), outside it in that folder, for another repository.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace;

public sealed partial class GitTools(WorkspaceScope scope)
{
    public const int MaxBlameLines = 200;
    private const int MaxOutputChars = 24_000;
    private const int MaxStatusEntries = 300;
    private const long MaxBlameFileBytes = 2_000_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly string[] GlobalArguments =
        ["--no-pager", "-c", "color.ui=never", "-c", "core.quotepath=false", "-c", "log.showSignature=false"];

    // Where git runs and what it is limited to; File is set when the path is an existing file.
    private sealed record GitTarget(string Directory, string Pathspec, string? File);

    // Text: stdout with "\n" line endings, cut when it grew past what a tool result can hold (Truncated).
    private sealed record GitOutput(string Text, bool Truncated, string? Error);

    // .git is a folder in a normal clone and a file in a worktree or submodule; a project may sit below the repository root.
    public static bool IsRepository(string root)
    {
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            var dotGit = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit)) return true;
        }
        return false;
    }

    public async Task<string> StatusAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!TryLocate(path, out var target, out var error)) return $"Error: {error}";
        var result = await RunGitAsync(target.Directory, ["status", "--short", "--branch", "--untracked-files=all", "--", target.Pathspec], cancellationToken);
        if (result.Error is not null) return $"Error: {result.Error}";

        var lines = WorkspaceTools.SplitLines(result.Text);
        var branch = lines.FirstOrDefault(l => l.StartsWith("## ", StringComparison.Ordinal)) is { } head ? $"Branch: {head[3..]}\n" : string.Empty;
        var files = lines.Where(l => !l.StartsWith("## ", StringComparison.Ordinal) && l.Length > 0).ToList();
        var under = path is null ? string.Empty : $" under '{path}'";
        if (files.Count == 0) return $"{branch}No uncommitted changes{under}: nothing staged, modified or untracked.";

        var builder = new StringBuilder(branch)
            .Append($"{files.Count}{(result.Truncated ? "+" : string.Empty)} changed file(s){under} (XY: X = staged, Y = not staged; ?? = untracked, R = renamed):\n");
        foreach (var line in files.Take(MaxStatusEntries)) builder.Append(line).Append('\n');
        if (files.Count > MaxStatusEntries || result.Truncated)
            builder.Append($"... (stopped at {MaxStatusEntries} entries; pass a path to narrow it)\n");
        builder.Append("git_diff shows the changes; untracked files are not in a diff, read them with read_file.");
        return Limit(builder.ToString(), "pass a path to narrow it");
    }

    // baseRevision: what the working tree (or, staged, the index) is compared with; default HEAD. stat: files only.
    public async Task<string> DiffAsync(string? path, string? baseRevision, bool staged, bool stat, CancellationToken cancellationToken = default)
    {
        if (!TryLocate(path, out var target, out var error)) return $"Error: {error}";
        if (baseRevision is not null && !IsRevision(baseRevision)) return RevisionError(baseRevision);

        List<string> arguments = ["diff", "--no-ext-diff", "--no-textconv", "--relative"];
        if (stat) arguments.Add("--stat=200");
        if (staged) arguments.Add("--cached");
        // Without a commit there is no HEAD to compare with; --cached alone compares the index with the empty tree.
        if (baseRevision is not null) arguments.Add(baseRevision);
        else if (!staged) arguments.Add("HEAD");
        arguments.AddRange(["--", target.Pathspec]);

        var result = await RunGitAsync(target.Directory, arguments, cancellationToken);
        if (result.Error is not null)
        {
            return baseRevision is null && NoCommitsYet(result.Error)
                ? "Error: the repository has no commits yet, so there is nothing to compare with. git_status lists the new files; read them with read_file."
                : $"Error: {result.Error}";
        }

        var against = baseRevision is null ? "HEAD" : $"'{baseRevision}'";
        var what = staged ? $"Staged changes (index against {against})" : $"Working tree against {against}{(baseRevision is null ? " (staged and not staged)" : string.Empty)}";
        var under = path is null ? string.Empty : $" under '{path}'";
        if (result.Text.Trim().Length == 0) return $"{what}{under}: no differences.";

        var untracked = staged ? string.Empty : "\nUntracked files are not in a diff: git_status lists them.";
        var output = $"{what}{under}{(stat ? ", files only" : string.Empty)}:\n{result.Text.TrimEnd()}{untracked}";
        return Limit(output, "pass a path, or stat: true for the list of changed files", result.Truncated);
    }

    // revision: a branch, tag or range ("main..HEAD"); query: words in the message; change: commits that added or
    // removed this text (git log -S); files: the changed files of each commit.
    public async Task<string> LogAsync(string? path, string? revision, int? limit, string? query, string? change, bool files, CancellationToken cancellationToken = default)
    {
        if (!TryLocate(path, out var target, out var error)) return $"Error: {error}";
        if (revision is not null && !IsRevision(revision)) return RevisionError(revision);

        var count = Math.Clamp(limit ?? 20, 1, 100);
        List<string> arguments = ["log", "--date=short", "--format=%h %ad %an: %s", $"--max-count={count}"];
        if (files) arguments.AddRange(["--name-status", "--relative"]);
        // A renamed file keeps its history.
        if (target.File is not null) arguments.Add("--follow");
        if (query is not null) arguments.AddRange([$"--grep={query}", "--regexp-ignore-case", "--fixed-strings"]);
        if (change is not null) arguments.Add($"-S{change}");
        if (revision is not null) arguments.Add(revision);
        arguments.AddRange(["--", target.Pathspec]);

        var result = await RunGitAsync(target.Directory, arguments, cancellationToken);
        if (result.Error is not null)
            return revision is null && NoCommitsYet(result.Error) ? "The repository has no commits yet." : $"Error: {result.Error}";
        if (result.Text.Trim().Length == 0) return "No commits match.";

        var output = $"Commits, newest first (hash date author: subject), at most {count}:\n{result.Text.TrimEnd()}";
        return Limit(output, "lower the limit or pass a path", result.Truncated);
    }

    public async Task<string> ShowAsync(string commit, string? path, bool stat, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commit)) return "Error: commit is empty.";
        commit = commit.Trim();
        if (!IsRevision(commit)) return RevisionError(commit);
        if (!TryLocate(path, out var target, out var error)) return $"Error: {error}";

        List<string> arguments = ["show", "--no-ext-diff", "--no-textconv", "--relative", "--format=medium", "--date=iso", "--stat=200"];
        if (!stat) arguments.Add("--patch");
        arguments.AddRange([commit, "--", target.Pathspec]);

        var result = await RunGitAsync(target.Directory, arguments, cancellationToken);
        if (result.Error is not null) return $"Error: {result.Error}";
        return Limit(result.Text.TrimEnd(), "pass a path, or stat: true for the list of changed files", result.Truncated);
    }

    public async Task<string> BlameAsync(string path, int? startLine, int? endLine, CancellationToken cancellationToken = default)
    {
        if (!TryLocate(path, out var target, out var error)) return $"Error: {error}";
        if (target.File is null) return $"Error: file '{path}' does not exist.";
        if (new FileInfo(target.File).Length > MaxBlameFileBytes) return $"Error: '{path}' is larger than 2 MB.";
        if (TextFileIo.LooksBinary(target.File)) return $"Error: '{path}' is a binary file.";

        var total = File.ReadLines(target.File).Count();
        if (total == 0) return $"File: {scope.ToDisplay(target.File)} (empty)";
        var from = Math.Clamp(startLine ?? 1, 1, total);
        var to = Math.Clamp(endLine ?? from + MaxBlameLines - 1, from, total);
        if (to - from + 1 > MaxBlameLines) to = from + MaxBlameLines - 1;

        var result = await RunGitAsync(target.Directory, ["blame", "--date=short", $"-L{from},{to}", "--", target.Pathspec], cancellationToken);
        if (result.Error is not null) return $"Error: {result.Error}";

        var builder = new StringBuilder()
            .Append($"Blame: {scope.ToDisplay(target.File)} (lines {from}-{to} of {total}; hash (author date line) code, 00000000 = not committed yet)\n")
            .Append(result.Text.TrimEnd()).Append('\n');
        if (to < total) builder.Append($"... {total - to} more line(s). Call git_blame with startLine={to + 1} to continue.\n");
        return Limit(builder.ToString(), "ask for fewer lines", result.Truncated);
    }

    // Inside the root git runs there with the path relative to it ("." = the whole project); outside it git runs in
    // the folder of the path, which may be another repository.
    private bool TryLocate(string? path, out GitTarget target, out string? error)
    {
        target = new GitTarget(scope.Root, ".", null);
        error = null;
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (!scope.TryResolveForRead(path, out var fullPath, out error)) return false;

        var file = File.Exists(fullPath) ? fullPath : null;
        if (scope.IsInside(fullPath))
        {
            target = new GitTarget(scope.Root, scope.ToRelative(fullPath), file);
            return true;
        }

        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            error = $"'{path}' does not exist.";
            return false;
        }
        target = new GitTarget(directory, file is null ? "." : Path.GetFileName(fullPath), file);
        return true;
    }

    // A commit, branch, tag or range such as "main", "HEAD~3", "a1b2c3d" or "main...HEAD": never an option.
    private static bool IsRevision(string value) => !value.StartsWith('-') && RevisionPattern().IsMatch(value);

    private static string RevisionError(string value) => $"Error: '{value}' is not a commit, branch, tag or range.";

    private static bool NoCommitsYet(string error) =>
        error.Contains("ambiguous argument 'HEAD'", StringComparison.Ordinal)
        || error.Contains("bad revision 'HEAD'", StringComparison.Ordinal)
        || error.Contains("does not have any commits yet", StringComparison.Ordinal);

    private static async Task<GitOutput> RunGitAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in GlobalArguments.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        // status would otherwise refresh the index; a read must not write to .git.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return new GitOutput(string.Empty, false, "git is not installed or not on PATH.");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        var stderr = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            var (text, truncated) = await ReadBoundedAsync(process.StandardOutput, limit.Token);
            if (truncated) Kill(process);
            await process.WaitForExitAsync(limit.Token);
            if (!truncated && process.ExitCode != 0)
            {
                var message = (await stderr).Trim();
                return new GitOutput(string.Empty, false, message.Length == 0 ? $"git exited with code {process.ExitCode}." : Clip(message, 600));
            }
            return new GitOutput(TextFileIo.NormalizeNewLines(text), truncated, null);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new GitOutput(string.Empty, false, $"git took longer than {Timeout.TotalSeconds:0} seconds.");
        }
    }

    // Stops reading once the output is longer than a tool result can hold; the caller then stops git.
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MaxOutputChars * 2) return (builder.ToString(), true);
        }
        return (builder.ToString(), false);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    // Keeps the head, cut at a line end: the start of a diff or log matters most.
    private static string Limit(string output, string hint, bool truncated = false)
    {
        if (output.Length <= MaxOutputChars && !truncated) return output;
        var cut = Math.Min(output.Length, MaxOutputChars);
        if (cut < output.Length && output.LastIndexOf('\n', cut - 1) is var end and > 0) cut = end;
        return output[..cut] + $"\n... (output truncated; {hint})";
    }

    private static string Clip(string text, int max) => text.Length > max ? text[..max] + "…" : text;

    [GeneratedRegex(@"^[\w./~^@{}+-]{1,200}$")]
    private static partial Regex RevisionPattern();
}

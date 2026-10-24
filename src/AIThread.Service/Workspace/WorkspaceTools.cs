using System.Text;
using System.Text.RegularExpressions;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace.Documents;
using Microsoft.Extensions.FileSystemGlobbing;

namespace AIThread.Service.Workspace;

// Read-only repository access for the planner. Output is plain text sized for a model context. Relative paths are
// relative to the project root; absolute paths may point outside it, for checking another folder or project.
// With secrets, the secrets in what is read come back as placeholders (SecretGuard); credential files are listed with a
// mark but never read (WorkspaceScope).
public sealed class WorkspaceTools(WorkspaceScope scope, ISecretGuard? secrets = null)
{
    private const string ProtectedMark = "  (protected: credential file)";

    public const int MaxReadLines = 600;
    private const int MaxOutputChars = 24_000;
    private const long MaxSearchFileBytes = 1_000_000;
    private const long MaxReadFileBytes = 2_000_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".cshtml", ".razor", ".vb", ".fs", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".svelte",
        ".json", ".xml", ".config", ".sql", ".css", ".scss", ".less", ".html", ".htm", ".md", ".txt", ".yml", ".yaml",
        ".csproj", ".vbproj", ".fsproj", ".props", ".targets", ".sln", ".slnx", ".resx", ".py", ".java", ".kt", ".go",
        ".rb", ".php", ".sh", ".ps1", ".psm1", ".bat", ".cmd", ".ini", ".toml", ".env", ".graphql", ".proto", ".dart"
    };

    public WorkspaceScope Scope => scope;

    // directoriesOnly: a folder map without files, so a deeper listing stays short.
    public string ListDirectory(string? path, int depth = 2, int maxEntries = 300, bool directoriesOnly = false)
    {
        if (!scope.TryResolveForRead(string.IsNullOrWhiteSpace(path) ? "." : path, out var fullPath, out var error)) return $"Error: {error}";
        if (!Directory.Exists(fullPath)) return $"Error: directory '{path}' does not exist.";

        depth = Math.Clamp(depth, 1, 4);
        var builder = new StringBuilder();
        var display = scope.ToDisplay(fullPath);
        builder.Append(display == "." ? "./" : display + "/").Append('\n');

        var count = 0;
        var truncated = false;
        Walk(fullPath, 1);
        if (truncated) builder.Append($"... (listing truncated at {maxEntries} entries; list a sub-directory for more)\n");
        return Limit(builder.ToString());

        void Walk(string directory, int level)
        {
            if (level > depth || truncated) return;
            string[] directories, files;
            try
            {
                directories = Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
                files = Directory.GetFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            var indent = new string(' ', level * 2);
            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                if (scope.IsIgnoredDirectoryPath(child)) continue;
                if (++count > maxEntries)
                {
                    truncated = true;
                    return;
                }
                builder.Append(indent).Append(name).Append("/\n");
                Walk(child, level + 1);
                if (truncated) return;
            }

            if (directoriesOnly) return;
            foreach (var file in files)
            {
                if (++count > maxEntries)
                {
                    truncated = true;
                    return;
                }
                builder.Append(indent).Append(Path.GetFileName(file)).Append(scope.IsProtected(file) ? ProtectedMark : string.Empty).Append('\n');
            }
        }
    }

    // path: where to search (default the project root). Inside the root, patterns stay relative to the root; outside it,
    // they are relative to that folder.
    public string FindFiles(string pattern, string? path = null, int maxResults = 200)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "Error: pattern is empty.";
        if (!TryResolveBase(path, out var baseDirectory, out var baseError)) return $"Error: {baseError}";

        var matcher = CreateMatcher(pattern);
        var results = new List<string>();
        foreach (var file in scope.EnumerateFiles(baseDirectory))
        {
            if (!matcher.Match(MatchPath(file, baseDirectory)).HasMatches) continue;
            results.Add(scope.ToDisplay(file) + (scope.IsProtected(file) ? ProtectedMark : string.Empty));
            if (results.Count >= maxResults) break;
        }

        if (results.Count == 0) return $"No files match '{pattern}'.";
        var suffix = results.Count >= maxResults ? $"\n... (stopped at {maxResults} results; use a narrower pattern)" : string.Empty;
        return Limit(string.Join('\n', results) + suffix);
    }

    public string SearchText(string query, string? fileGlob, bool isRegex, int maxMatches = 80, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Error: query is empty.";
        if (!TryResolveBase(path, out var baseDirectory, out var baseError)) return $"Error: {baseError}";

        Regex? regex = null;
        if (isRegex)
        {
            try
            {
                regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                return $"Error: invalid regex: {ex.Message}";
            }
        }

        var matcher = string.IsNullOrWhiteSpace(fileGlob) ? null : CreateMatcher(fileGlob);
        var builder = new StringBuilder();
        var matches = 0;
        var filesWithMatches = 0;

        foreach (var file in scope.EnumerateFiles(baseDirectory))
        {
            var display = scope.ToDisplay(file);
            if (matcher is not null ? !matcher.Match(MatchPath(file, baseDirectory)).HasMatches : !TextExtensions.Contains(Path.GetExtension(file))) continue;
            if (scope.IsProtected(file)) continue;

            try
            {
                if (new FileInfo(file).Length > MaxSearchFileBytes || TextFileIo.LooksBinary(file)) continue;

                var lineNumber = 0;
                var fileMatched = false;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    var hit = regex?.IsMatch(line) ?? line.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (!hit) continue;

                    if (!fileMatched)
                    {
                        fileMatched = true;
                        filesWithMatches++;
                    }
                    var shown = secrets?.RedactFile(file, line.Trim(), scope.Secrets.Keys) ?? line.Trim();
                    builder.Append(display).Append(':').Append(lineNumber).Append(": ").Append(Truncate(shown, 200)).Append('\n');
                    if (++matches >= maxMatches)
                    {
                        builder.Append($"... (stopped at {maxMatches} matches; refine the query or file glob)\n");
                        return Limit(builder.ToString());
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
            {
                // Unreadable file or pathological regex — skip it.
            }
        }

        return matches == 0
            ? $"No matches for '{query}'."
            : Limit($"{matches} match(es) in {filesWithMatches} file(s):\n{builder}");
    }

    public async Task<string> ReadFileAsync(string path, int? startLine, int? endLine, CancellationToken cancellationToken = default)
    {
        if (!scope.TryResolveForRead(path, out var fullPath, out var error)) return $"Error: {error}";
        if (Directory.Exists(fullPath)) return $"Error: '{path}' is a directory. Use list_directory.";
        if (!File.Exists(fullPath)) return $"Error: file '{path}' does not exist.";

        var extension = Path.GetExtension(fullPath);
        if (DocumentExtractors.LegacyFormatMessage(extension) is { } legacy) return $"Error: {legacy}";

        string content;
        var extracted = false;
        if (DocumentExtractors.TryGet(extension, out var extractor))
        {
            if (new FileInfo(fullPath).Length > DocumentExtractors.MaxDocumentBytes) return $"Error: '{path}' is larger than 20 MB.";
            try
            {
                content = TextFileIo.NormalizeNewLines(await extractor.ExtractAsync(fullPath, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"Error: could not extract text from '{path}': {ex.Message}";
            }
            extracted = true;
        }
        else
        {
            if (new FileInfo(fullPath).Length > MaxReadFileBytes) return $"Error: '{path}' is larger than 2 MB.";
            if (TextFileIo.LooksBinary(fullPath)) return $"Error: '{path}' is a binary file.";
            content = (await TextFileIo.ReadAsync(fullPath, cancellationToken)).Content;
        }

        if (secrets is not null) content = secrets.RedactFile(fullPath, content, scope.Secrets.Keys);
        var lines = SplitLines(content);
        var total = lines.Length;
        if (total == 0) return $"File: {scope.ToDisplay(fullPath)} (empty)";

        var from = Math.Clamp(startLine ?? 1, 1, total);
        var to = Math.Clamp(endLine ?? from + MaxReadLines - 1, from, total);
        if (to - from + 1 > MaxReadLines) to = from + MaxReadLines - 1;

        var builder = new StringBuilder();
        builder.Append(extracted
            ? $"File: {scope.ToDisplay(fullPath)} (extracted text, lines {from}-{to} of {total})\n"
            : $"File: {scope.ToDisplay(fullPath)} (lines {from}-{to} of {total})\n");
        for (var i = from; i <= to; i++) builder.Append(i).Append("| ").Append(lines[i - 1]).Append('\n');
        if (to < total) builder.Append($"... {total - to} more line(s). Call read_file with startLine={to + 1} to continue.\n");
        return Limit(builder.ToString());
    }

    // Lines without the phantom empty entry produced by a trailing newline.
    public static string[] SplitLines(string content)
    {
        if (content.Length == 0) return [];
        var lines = content.Split('\n');
        return content.EndsWith('\n') ? lines[..^1] : lines;
    }

    // The folder a search starts in: the project root, or a folder given by the planner (possibly outside the root).
    private bool TryResolveBase(string? path, out string directory, out string? error)
    {
        directory = scope.Root;
        error = null;
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (!scope.TryResolveForRead(path, out directory, out error)) return false;
        if (Directory.Exists(directory)) return true;

        error = $"directory '{path}' does not exist.";
        return false;
    }

    private string MatchPath(string file, string baseDirectory) =>
        scope.IsInside(file) ? scope.ToRelative(file) : Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');

    private static Matcher CreateMatcher(string pattern)
    {
        var normalized = pattern.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        if (!normalized.Contains('/')) normalized = "**/" + normalized;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(normalized);
        return matcher;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private static string Limit(string output) =>
        output.Length <= MaxOutputChars ? output : output[..MaxOutputChars] + "\n... (output truncated)";
}

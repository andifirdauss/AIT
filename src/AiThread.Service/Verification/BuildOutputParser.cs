using System.Text.RegularExpressions;

namespace AiThread.Service.Verification;

// One compiler/build message. File is a full path when the message names one.
public sealed record BuildDiagnostic(string Severity, string? File, int? Line, int? Column, string? Code, string Message)
{
    public bool IsError => Severity == "error";

    // Copying to bin/ failed because a running program holds the file (often the app being worked on): the code
    // itself compiled, so this is no reason to change it.
    public bool IsLockedOutput => Code is "MSB3021" or "MSB3026" or "MSB3027";
}

// Reads build output line by line and keeps the errors (deduplicated: MSBuild repeats them per target framework
// and in its summary) plus a warning count. Understands MSBuild/Roslyn and tsc ("file(line,col): error CODE: msg"),
// MSBuild messages without a location, Maven ("[ERROR] file:[line,col] msg") and "file:line:col: msg" (Go, Rust
// short format, gcc, javac). Anything else is left to the output tail.
public sealed partial class BuildOutputParser(string workingDirectory)
{
    public const int MaxErrors = 200;

    private readonly List<BuildDiagnostic> _errors = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BuildDiagnostic> Errors => _errors;
    public int WarningCount { get; private set; }

    public void Add(string line)
    {
        if (Parse(line) is not { } diagnostic) return;
        if (!_seen.Add($"{diagnostic.Severity}|{diagnostic.File}|{diagnostic.Line}|{diagnostic.Column}|{diagnostic.Code}|{diagnostic.Message}")) return;

        if (diagnostic.IsError)
        {
            if (_errors.Count < MaxErrors) _errors.Add(diagnostic);
        }
        else
        {
            WarningCount++;
        }
    }

    public BuildDiagnostic? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 4000) return null;

        var match = MsBuildLocated().Match(line);
        if (match.Success)
            return Create(match.Groups["sev"].Value, match.Groups["file"].Value, match.Groups["line"].Value, match.Groups["col"].Value, match.Groups["code"].Value, match.Groups["msg"].Value);

        match = MsBuildGeneral().Match(line);
        if (match.Success)
        {
            var origin = match.Groups["origin"].Value.Trim();
            var file = LooksLikePath(origin) ? origin : null;
            return Create(match.Groups["sev"].Value, file, null, null, match.Groups["code"].Value, match.Groups["msg"].Value);
        }

        match = Maven().Match(line);
        if (match.Success)
        {
            var file = match.Groups["file"].Value;
            if (DriveSlash().IsMatch(file)) file = file[1..];
            return Create(match.Groups["sev"].Value, file, match.Groups["line"].Value, match.Groups["col"].Value, null, match.Groups["msg"].Value);
        }

        match = Colon().Match(line);
        if (match.Success && LooksLikePath(match.Groups["file"].Value))
        {
            var message = match.Groups["msg"].Value.Trim();
            var severity = "error";
            string? code = null;

            var prefix = SeverityPrefix().Match(message);
            if (prefix.Success)
            {
                var label = prefix.Groups["sev"].Value.ToLowerInvariant();
                if (label is "note" or "help") return null;
                severity = label == "warning" ? "warning" : "error";
                code = prefix.Groups["code"].Success ? prefix.Groups["code"].Value : null;
                message = message[prefix.Length..].Trim();
            }

            return Create(severity, match.Groups["file"].Value, match.Groups["line"].Value, match.Groups["col"].Value, code, message);
        }

        return null;
    }

    private BuildDiagnostic Create(string severity, string? file, string? line, string? column, string? code, string message) => new(
        severity.StartsWith("warn", StringComparison.OrdinalIgnoreCase) ? "warning" : "error",
        FullPath(file),
        int.TryParse(line, out var l) ? l : null,
        int.TryParse(column, out var c) ? c : null,
        string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
        message.Trim());

    private string? FullPath(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            var trimmed = file.Trim();
            return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(workingDirectory, trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return file.Trim();
        }
    }

    // "MSBUILD", "CSC" or a timestamp are origins, not files: a file has an extension.
    private static bool LooksLikePath(string value) => FileWithExtension().IsMatch(value.Trim());

    [GeneratedRegex(@"^\s*(?<file>[^\r\n]+?)\((?<line>\d+)(?:,(?<col>\d+))?(?:,\d+,\d+)?\)\s*:\s*(?<sev>error|warning)\s*(?<code>[A-Za-z]+\d+)?\s*:\s*(?<msg>.*?)(?:\s+\[[^\[\]]+\])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MsBuildLocated();

    [GeneratedRegex(@"^\s*(?<origin>[^\r\n]*?)\s*:\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*?)(?:\s+\[[^\[\]]+\])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MsBuildGeneral();

    [GeneratedRegex(@"^\[(?<sev>ERROR|WARNING)\]\s+(?<file>.+?):\[(?<line>\d+),(?<col>\d+)\]\s*(?<msg>.+)$")]
    private static partial Regex Maven();

    [GeneratedRegex(@"^\s*(?<file>(?:[A-Za-z]:)?[^:\r\n]+?):(?<line>\d+):(?:(?<col>\d+):)?\s*(?<msg>.+)$")]
    private static partial Regex Colon();

    [GeneratedRegex(@"^(?<sev>error|warning|note|help)(?:\[(?<code>[A-Za-z0-9_]+)\])?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex SeverityPrefix();

    [GeneratedRegex(@"^/[A-Za-z]:")]
    private static partial Regex DriveSlash();

    [GeneratedRegex(@"[^\s\\/]\.[A-Za-z0-9]{1,10}$")]
    private static partial Regex FileWithExtension();
}

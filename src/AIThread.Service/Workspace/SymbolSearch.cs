// Planner tools find_symbol / find_references.
// C# is parsed with Roslyn syntax only (CSharpSymbols); JS/TS/Python/Java use regex patterns (SymbolPatterns).
// Read-only. Paths are resolved through WorkspaceScope, like WorkspaceTools.

using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace;

public sealed class SymbolSearch(WorkspaceScope scope)
{
    private const int MaxOutputChars = 24_000;
    private const long MaxFileBytes = 1_000_000;

    public string FindSymbol(string name, string? kind = null, string? path = null, int maxResults = 60)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Error: name is empty.";

        string member;
        string? container;
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            member = name[(dot + 1)..];
            var prev = name.LastIndexOf('.', dot - 1 < 0 ? 0 : dot - 1);
            container = dot - 1 < 0 ? name[..dot] : name[(prev + 1)..dot];
            if (dot == 0) container = string.Empty;
            if (prev < 0 && dot > 0) container = name[..dot];
        }
        else
        {
            member = name;
            container = null;
        }

        if (!TryResolveBase(path, out var baseDirectory, out var baseError)) return $"Error: {baseError}";

        var builder = new System.Text.StringBuilder();
        var count = 0;

        foreach (var file in scope.EnumerateFiles(baseDirectory))
        {
            var ext = Path.GetExtension(file);
            if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) && !SymbolPatterns.Supports(ext)) continue;

            try
            {
                if (new FileInfo(file).Length > MaxFileBytes || TextFileIo.LooksBinary(file)) continue;

                var content = TextFileIo.NormalizeNewLines(File.ReadAllText(file));
                if (!content.Contains(member, StringComparison.Ordinal)) continue;

                var declarations = string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)
                    ? CSharpSymbols.FindDeclarations(content, member)
                    : SymbolPatterns.FindDeclarations(ext, WorkspaceTools.SplitLines(content), member);

                var display = scope.ToDisplay(file);
                foreach (var d in declarations)
                {
                    if (container is not null && !string.Equals(d.Container, container, StringComparison.Ordinal)) continue;
                    if (kind is not null && !KindMatches(d.Kind, kind)) continue;

                    if (count >= maxResults)
                    {
                        builder.Append($"... (stopped at {maxResults} results; narrow with kind, path or Type.Member)\n");
                        return Limit($"{count} declaration(s):\n{builder}");
                    }

                    builder.Append(display).Append(':').Append(d.Line).Append(": ").Append(d.Kind).Append(' ').Append(d.Signature);
                    if (d.Container is not null) builder.Append(" (in ").Append(d.Container).Append(')');
                    builder.Append('\n');
                    count++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable file — skip it.
            }
        }

        return count == 0
            ? $"No declarations of '{name}'."
            : Limit($"{count} declaration(s):\n{builder}");
    }

    public string FindReferences(string name, string? path = null, int maxResults = 120)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Error: name is empty.";

        var dot = name.LastIndexOf('.');
        var member = dot >= 0 ? name[(dot + 1)..] : name;
        if (member.Length == 0) return "Error: name is empty.";

        if (!TryResolveBase(path, out var baseDirectory, out var baseError)) return $"Error: {baseError}";

        var pattern = @"(?<![\w$])" + Regex.Escape(member) + @"(?![\w$])";
        var builder = new System.Text.StringBuilder();
        var count = 0;
        var filesWithMatches = 0;

        foreach (var file in scope.EnumerateFiles(baseDirectory))
        {
            var ext = Path.GetExtension(file);
            var isCSharp = string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase);
            if (!isCSharp && !SymbolPatterns.Supports(ext)) continue;

            try
            {
                if (new FileInfo(file).Length > MaxFileBytes || TextFileIo.LooksBinary(file)) continue;

                var content = TextFileIo.NormalizeNewLines(File.ReadAllText(file));
                if (!content.Contains(member, StringComparison.Ordinal)) continue;

                var display = scope.ToDisplay(file);
                var fileMatched = false;

                if (isCSharp)
                {
                    foreach (var r in CSharpSymbols.FindReferences(content, member))
                    {
                        if (!fileMatched)
                        {
                            fileMatched = true;
                            filesWithMatches++;
                        }

                        if (count >= maxResults)
                        {
                            builder.Append($"... (stopped at {maxResults} references; narrow with path)\n");
                            return Limit($"{count} reference(s) in {filesWithMatches} file(s):\n{builder}");
                        }

                        builder.Append(display).Append(':').Append(r.Line).Append(": ").Append(r.Text);
                        if (r.IsDeclaration) builder.Append(" [declaration]");
                        builder.Append('\n');
                        count++;
                    }
                }
                else
                {
                    var lines = WorkspaceTools.SplitLines(content);
                    var declaredLines = new HashSet<int>(
                        SymbolPatterns.FindDeclarations(ext, lines, member).Select(d => d.Line));
                    var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

                    for (var i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (SymbolPatterns.IsCommentLine(ext, line)) continue;
                        if (!regex.IsMatch(line)) continue;

                        var lineNumber = i + 1;
                        if (!fileMatched)
                        {
                            fileMatched = true;
                            filesWithMatches++;
                        }

                        if (count >= maxResults)
                        {
                            builder.Append($"... (stopped at {maxResults} references; narrow with path)\n");
                            return Limit($"{count} reference(s) in {filesWithMatches} file(s):\n{builder}");
                        }

                        builder.Append(display).Append(':').Append(lineNumber).Append(": ").Append(Truncate(line.Trim(), 200));
                        if (declaredLines.Contains(lineNumber)) builder.Append(" [declaration]");
                        builder.Append('\n');
                        count++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
            {
                // Unreadable file or pathological regex — skip it.
            }
        }

        return count == 0
            ? $"No references to '{name}'."
            : Limit($"{count} reference(s) in {filesWithMatches} file(s):\n{builder}");
    }

    private static bool KindMatches(string declarationKind, string filter)
    {
        if (string.Equals(filter, "type", StringComparison.OrdinalIgnoreCase))
        {
            return declarationKind is "class" or "struct" or "record" or "interface" or "enum" or "delegate" or "type alias";
        }

        return string.Equals(declarationKind, filter, StringComparison.OrdinalIgnoreCase);
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

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private static string Limit(string output) =>
        output.Length <= MaxOutputChars ? output : output[..MaxOutputChars] + "\n... (output truncated)";
}

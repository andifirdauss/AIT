using System.Text;
using AiThread.Common.Enums;
using AiThread.Service.CodeCalls;
using AiThread.Service.Implementations;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

// Planner tools code_outline and function_calls, and the outline of the files a request names in the planner brief:
// the skeleton of the code (types, members and their line ranges) and the calls the tools node recorded, so the
// planner reads only the lines it needs. A file is outlined from disk on every call; the project snapshot (Struktur
// kode) is only used to list a folder and to find a class by name.
public sealed class CodeSkeletonTools(CodeStructureService structure, FunctionCallStore store, FunctionCallQueue queue)
{
    private const int MaxOutputChars = 24_000;
    private const int MaxFolderFiles = 300;
    private const int MaxFunctionsListed = 12;
    private const int MaxClassFiles = 6;
    private const int MaxClassMatches = 40;
    private const int MaxOverloads = 5;
    private const int MaxCallers = 40;

    public async Task<string> OutlineAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken)
    {
        if (path is null && name is null) return "Error: give path (a file or folder) or name (a class).";
        try
        {
            if (path is null) return await ClassAsync(project, scope, name!, cancellationToken);
            if (!scope.TryResolveForRead(path, out var full, out var error)) return $"Error: {error}";
            if (Directory.Exists(full)) return await FolderAsync(project, scope, full, cancellationToken);
            if (!File.Exists(full)) return $"Error: '{path}' does not exist.";
            return Limit(await FileAsync(project, scope, full, name, cancellationToken));
        }
        catch (AppValidationException)
        {
            return "Error: the project could not be scanned (its folder is missing on this machine).";
        }
    }

    public async Task<string> CallsAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken)
    {
        if (path is null || name is null) return "Error: path and name are required.";
        if (!scope.TryResolve(path, out var full, out var error)) return $"Error: {error} Function calls are recorded for project files only.";
        if (!File.Exists(full)) return $"Error: '{path}' does not exist.";
        var relative = scope.ToRelative(full);
        if (Read(full) is not { } file) return $"Error: no outline for '{relative}' (C#, PHP, JavaScript/TypeScript, Python, Java and Kotlin files up to 2 MB only).";

        var spans = Matching(FunctionSpans.Of(file.Outline), name);
        if (spans.Count == 0) return $"No function or method '{name}' with a body in {relative}. code_outline lists its declarations.";

        var builder = new StringBuilder();
        foreach (var span in spans.Take(MaxOverloads))
        {
            builder.Append($"{span.Symbol}{(span.Signature is null ? string.Empty : $" — {span.Signature}")}\n{relative}:{span.Line}-{span.EndLine}\n");
            var key = FunctionSpans.Key(relative, span.Symbol, span.Signature);
            var scan = await store.GetAsync(project.ProjectId, key, cancellationToken);
            if (scan is not { Status: FunctionScanStatus.Done })
            {
                queue.Read(project.ProjectId, project.RootPath, relative, span.Line, span.EndLine);
                builder.Append("Calls: not recorded yet (queued for the tools node, which reads them in the background). Read the function if you need them now.\n");
            }
            else
            {
                var changed = scan.BodyHash != FunctionSpans.Hash(Body(file.Lines, span));
                if (changed) queue.Read(project.ProjectId, project.RootPath, relative, span.Line, span.EndLine);
                builder.Append($"Calls (recorded {scan.UpdatedAt ?? scan.CreatedAt:yyyy-MM-dd HH:mm} UTC{(changed ? "; the function changed since, so call lines may be off: read it before relying on them" : string.Empty)}):\n");
                if (scan.Calls.Count == 0) builder.Append("  (none into the project's own code)\n");
                foreach (var call in scan.Calls)
                {
                    builder.Append($"  {call.Line}: {call.Kind.ToString().ToLowerInvariant()} {call.Target}");
                    builder.Append(call.ResolvedPath is { } target
                        ? $" → {target}:{call.ResolvedLine}{(call.ResolvedSymbol is { } symbol ? " " + symbol : string.Empty)}\n"
                        : " → not found in the project\n");
                }
            }

            var callers = await store.CallersAsync(project.ProjectId, relative, CallTarget(span.Symbol), MaxCallers, cancellationToken);
            builder.Append(callers.Count == 0
                ? "Called by: none recorded (only functions read so far are known; find_references lists every use).\n"
                : "Called by (recorded so far; find_references lists every use):\n");
            foreach (var caller in callers) builder.Append($"  {caller.Path}:{caller.Line} {caller.Symbol}\n");
            builder.Append('\n');
        }
        if (spans.Count > MaxOverloads) builder.Append($"... {spans.Count - MaxOverloads} more overload(s).\n");
        return Limit(builder.ToString().TrimEnd() + "\n");
    }

    // The planner brief: the outline of each file the request names, until the budget is spent.
    public async Task<string?> BriefAsync(ProjectContext project, WorkspaceScope scope, IEnumerable<string> paths, int maxFiles, int maxChars, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var path in paths.Take(maxFiles))
        {
            if (OutlineLanguages.Of(path) is null || !scope.TryResolve(path, out var full, out _) || !File.Exists(full)) continue;
            string outline;
            try
            {
                outline = await FileAsync(project, scope, full, null, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (outline.StartsWith("Error:", StringComparison.Ordinal)) continue;
            if (builder.Length + outline.Length > maxChars)
            {
                builder.Append($"- {scope.ToRelative(full)}: outline too long for the brief; call code_outline.\n");
                continue;
            }
            builder.Append(outline).Append('\n');
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    private async Task<string> FileAsync(ProjectContext project, WorkspaceScope scope, string full, string? name, CancellationToken cancellationToken)
    {
        var display = scope.ToDisplay(full);
        if (Read(full) is not { } file)
            return $"Error: no outline for '{display}' (C#, PHP, JavaScript/TypeScript, Python, Java and Kotlin files up to 2 MB only). Use read_file.";

        var types = name is null ? file.Outline.Types : file.Outline.Types.Where(t => TypeMatches(t, name)).ToList();
        if (name is not null && types.Count == 0) return $"No type '{name}' in {display}.";
        var functions = name is null ? file.Outline.Functions : [];

        var calls = scope.IsInside(full)
            ? await CallNotesAsync(project.ProjectId, scope.ToRelative(full), file, types, functions, cancellationToken)
            : new Dictionary<OutlineMember, string>();
        var builder = new StringBuilder();
        builder.Append($"{display} ({file.Language}, {file.Lines.Length} lines)\n");
        string? currentNamespace = null;
        foreach (var type in types)
        {
            if (type.Namespace is { } ns && ns != currentNamespace) builder.Append($"namespace {ns}\n");
            currentNamespace = type.Namespace;
            builder.Append($"{type.Kind} {(type.Container is null ? type.Name : $"{type.Container}.{type.Name}")}{(type.IsPartial ? " (partial)" : string.Empty)} {Range(type.Line, type.EndLine)}\n");
            foreach (var member in type.Members) AppendMember(builder, member, calls);
        }
        if (functions.Count > 0)
        {
            builder.Append("functions\n");
            foreach (var function in functions) AppendMember(builder, function, calls);
        }
        if (types.Count == 0 && functions.Count == 0) builder.Append("No declarations found; use read_file.\n");
        return builder.ToString();
    }

    private static void AppendMember(StringBuilder builder, OutlineMember member, IReadOnlyDictionary<OutlineMember, string> calls)
    {
        // PHP and JavaScript signatures start with "function" already.
        var signature = member.Signature ?? member.Name;
        var kind = signature.StartsWith(member.Kind + " ", StringComparison.Ordinal) ? string.Empty : member.Kind + " ";
        builder.Append($"  {Range(member.Line, member.EndLine)} {kind}{signature}");
        if (calls.TryGetValue(member, out var note)) builder.Append($" [{note}]");
        builder.Append('\n');
    }

    // "calls: N" for the functions whose calls the tools node recorded (function_calls lists them).
    private async Task<IReadOnlyDictionary<OutlineMember, string>> CallNotesAsync(Guid projectId, string relative, OutlinedFile file,
        IReadOnlyList<OutlineType> types, IReadOnlyList<OutlineMember> functions, CancellationToken cancellationToken)
    {
        var keyed = types.SelectMany(t => t.Members.Select(m => (Member: m, Symbol: FunctionSpans.SymbolOf(t.Name, t.Container, m.Name))))
            .Concat(functions.Select(f => (Member: f, Symbol: f.Name)))
            .Where(m => FunctionSpans.IsScanned(m.Member))
            .Select(m => (m.Member, Key: FunctionSpans.Key(relative, m.Symbol, m.Member.Signature)))
            .ToList();
        var notes = new Dictionary<OutlineMember, string>(ReferenceEqualityComparer.Instance);
        if (keyed.Count == 0) return notes;
        var states = await store.StatesAsync(projectId, keyed.Select(k => k.Key).ToList(), cancellationToken);
        foreach (var (member, key) in keyed)
        {
            if (!states.TryGetValue(key, out var state) || state.Status != FunctionScanStatus.Done) continue;
            var changed = state.BodyHash != FunctionSpans.Hash(Body(file.Lines, new FunctionSpan(member.Name, member.Name, member.Signature, member.Line, member.EndLine)));
            notes[member] = $"calls: {state.Calls}{(changed ? ", changed since" : string.Empty)}";
        }
        return notes;
    }

    // One line per outlined file under the folder: its types with their member counts and its functions.
    private async Task<string> FolderAsync(ProjectContext project, WorkspaceScope scope, string full, CancellationToken cancellationToken)
    {
        if (!scope.IsInside(full)) return "Error: folder outlines cover the project only; use list_directory outside it.";
        var snapshot = await structure.GetSnapshotAsync(project.ProjectId, cancellationToken);
        var relative = scope.ToRelative(full);
        var prefix = relative == "." ? string.Empty : relative + "/";
        var files = snapshot.Files.Values
            .Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) return $"No files under '{(prefix.Length == 0 ? "." : relative)}' in the project outline.";

        var outlined = files.Where(f => f.Outline is { } o && (o.Types.Count > 0 || o.Functions.Count > 0)).ToList();
        var thirdParty = files.Count(f => f.ThirdParty);
        var builder = new StringBuilder();
        builder.Append($"{(prefix.Length == 0 ? "Project" : relative)}: {files.Count} files, {outlined.Count} with declarations");
        builder.Append(thirdParty > 0 ? $", {thirdParty} third-party (not outlined)" : string.Empty);
        builder.Append(". Member counts in (); call code_outline with a file path for line ranges and signatures.\n");
        var shown = 0;
        foreach (var file in outlined)
        {
            if (shown == MaxFolderFiles || builder.Length > MaxOutputChars) break;
            var outline = file.Outline!;
            var parts = outline.Types.Select(t => $"{t.Kind} {(t.Container is null ? t.Name : $"{t.Container}.{t.Name}")} ({t.Members.Count})").ToList();
            if (outline.Functions.Count > 0)
                parts.Add("functions " + string.Join(", ", outline.Functions.Take(MaxFunctionsListed).Select(f => f.Name))
                          + (outline.Functions.Count > MaxFunctionsListed ? $" +{outline.Functions.Count - MaxFunctionsListed}" : string.Empty));
            builder.Append($"- {file.Path}: {string.Join("; ", parts)}\n");
            shown++;
        }
        if (shown < outlined.Count) builder.Append($"... {outlined.Count - shown} more files with declarations: outline a subfolder.\n");
        if (snapshot.Truncated) builder.Append($"(The project has more than {CodeStructureScanner.MaxFiles} files; the outline covers the first ones.)\n");
        return builder.ToString();
    }

    // "UserService" or "Outer.Inner": every file that declares it (a partial class has several), outlined from disk.
    private async Task<string> ClassAsync(ProjectContext project, WorkspaceScope scope, string name, CancellationToken cancellationToken)
    {
        var snapshot = await structure.GetSnapshotAsync(project.ProjectId, cancellationToken);
        var dot = name.LastIndexOf('.');
        var typeName = dot < 0 ? name : name[(dot + 1)..];
        var matches = snapshot.Symbols.Types(typeName).Where(m => TypeMatches(m.Type, name)).ToList();
        if (matches.Any(m => m.Type.Name == typeName)) matches = matches.Where(m => m.Type.Name == typeName).ToList();
        if (matches.Count == 0) return $"No type named '{name}' in the project outline. Try find_symbol, or give path.";

        var paths = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count > MaxClassFiles)
        {
            var list = new StringBuilder($"{paths.Count} files declare a type named '{name}'; call code_outline with path and name for one of them:\n");
            foreach (var (path, type) in matches.Take(MaxClassMatches))
                list.Append($"- {path}:{type.Line} {type.Kind} {(type.Container is null ? type.Name : $"{type.Container}.{type.Name}")}{(type.Namespace is null ? string.Empty : $" ({type.Namespace})")}\n");
            return list.ToString();
        }

        var builder = new StringBuilder();
        foreach (var path in paths)
            builder.Append(await FileAsync(project, scope, Path.Combine(scope.Root, path), name, cancellationToken)).Append('\n');
        return Limit(builder.ToString().TrimEnd() + "\n");
    }

    private sealed record OutlinedFile(string Language, string[] Lines, FileOutline Outline);

    private static OutlinedFile? Read(string full)
    {
        var language = OutlineLanguages.Of(full);
        if (language is null || new FileInfo(full).Length > CodeStructureScanner.MaxParseBytes) return null;
        // The same reading as the call scanner, so body hashes compare.
        var content = File.ReadAllText(full);
        return new OutlinedFile(language, WorkspaceTools.SplitLines(content).Select(l => l.TrimEnd('\r')).ToArray(), OutlineLanguages.Outline(language, content));
    }

    // "Save", "UserService.Save" or "Outer.Inner.Save"; exact case first.
    private static List<FunctionSpan> Matching(IReadOnlyList<FunctionSpan> spans, string name)
    {
        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            var found = spans.Where(s => s.Symbol.Equals(name, comparison) || s.Name.Equals(name, comparison) || s.Symbol.EndsWith("." + name, comparison)).ToList();
            if (found.Count > 0) return found;
        }
        return [];
    }

    private static bool TypeMatches(OutlineType type, string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0) return type.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        var qualifier = name[..dot];
        return type.Name.Equals(name[(dot + 1)..], StringComparison.OrdinalIgnoreCase)
               && (string.Equals(type.Container, qualifier, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(type.Namespace, qualifier, StringComparison.OrdinalIgnoreCase));
    }

    // Calls are resolved to "Type.Member" (without the outer type of a nested one) or a bare function name.
    private static string CallTarget(string symbol)
    {
        var parts = symbol.Split('.');
        return parts.Length <= 2 ? symbol : $"{parts[^2]}.{parts[^1]}";
    }

    private static string Body(string[] lines, FunctionSpan span) =>
        string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));

    private static string Range(int line, int endLine) => endLine > line ? $"{line}-{endLine}" : line.ToString();

    private static string Limit(string text) =>
        text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "\n... (truncated: outline a smaller part, or give name)\n";
}

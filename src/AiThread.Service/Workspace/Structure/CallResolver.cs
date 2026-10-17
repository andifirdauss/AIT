using System.Text.RegularExpressions;
using AiThread.Common.Enums;

namespace AiThread.Service.Workspace.Structure;

public sealed record SymbolLocation(string Path, string Symbol, int Line);

// Declarations of a snapshot by name, for resolving the calls of a function: functions and members by their own name,
// types by name, files by path. Names compare without case (PHP and URLs ignore it); exact case wins among equals.
public sealed class StructureSymbolIndex
{
    private readonly Dictionary<string, List<SymbolLocation>> _callables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Path, OutlineType Type)>> _types = new(StringComparer.OrdinalIgnoreCase);

    private StructureSymbolIndex(IReadOnlyCollection<string> files) => Files = files;

    public IReadOnlyCollection<string> Files { get; }

    public static StructureSymbolIndex Build(StructureSnapshot snapshot)
    {
        var index = new StructureSymbolIndex(snapshot.Files.Keys.ToList());
        foreach (var file in snapshot.Files.Values)
        {
            if (file.Outline is null) continue;
            index.Add(file.Path, file.Outline);
        }
        return index;
    }

    public void Add(string path, FileOutline outline)
    {
        foreach (var type in outline.Types)
        {
            List(_types, type.Name).Add((path, type));
            foreach (var member in type.Members) List(_callables, member.Name).Add(new SymbolLocation(path, $"{type.Name}.{member.Name}", member.Line));
        }
        foreach (var function in outline.Functions) List(_callables, function.Name).Add(new SymbolLocation(path, function.Name, function.Line));
    }

    public IReadOnlyList<SymbolLocation> Callables(string name) => _callables.GetValueOrDefault(name) ?? [];

    public IReadOnlyList<(string Path, OutlineType Type)> Types(string name) => _types.GetValueOrDefault(name) ?? [];

    private static List<T> List<T>(Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        return list;
    }
}

// Finds where a call made inside a function is declared: the calling file first, then the type the call names, then
// a name that is unique in the project (or whose file names the qualifier, as clsGlobal.showLoading → clsGlobal.js).
// URLs go to Controller.Action (ASP.NET MVC, areas and all) or to a script file of that path. Null when unsure:
// a wrong link is worse than none.
public static partial class CallResolver
{
    private static readonly HashSet<string> SelfQualifiers = new(StringComparer.OrdinalIgnoreCase) { "this", "self", "static", "parent", "base", "$this", "me" };
    private static readonly string[] ViewExtensions = [".cshtml", ".razor", ".vbhtml", ".aspx", ".ascx", ".mustache", ".blade.php", ".php", ".html", ".twig"];
    private static readonly string[] ScriptExtensions = [".php", ".aspx", ".ashx", ".asmx", ".svc", ".html", ".htm", ".jsp", ".py"];

    public static SymbolLocation? Resolve(CodeCallKind kind, string target, string callerPath, FileOutline callerOutline, StructureSymbolIndex index) => kind switch
    {
        CodeCallKind.Http => ResolveUrl(target, index),
        CodeCallKind.View => ResolveView(target, index),
        CodeCallKind.Sql => null,
        _ => ResolveCall(target, callerPath, callerOutline, index)
    };

    private static SymbolLocation? ResolveCall(string target, string callerPath, FileOutline callerOutline, StructureSymbolIndex index)
    {
        var parts = Qualified(target);
        if (parts.Count == 0) return null;
        var name = parts[^1];
        var qualifier = parts.Count > 1 ? parts[^2] : null;

        // Unqualified or self-qualified: the calling file knows it best (its copy may be newer than the snapshot).
        if (qualifier is null || SelfQualifiers.Contains(qualifier))
        {
            if (InFile(callerPath, callerOutline, name) is { } local) return local;
            if (qualifier is not null) return null;
        }

        // A type of that name: its member, from any of its files (partial classes).
        if (qualifier is not null)
        {
            foreach (var (path, type) in index.Types(qualifier))
            {
                if (type.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } member)
                    return new SymbolLocation(path, $"{type.Name}.{member.Name}", member.Line);
            }
        }

        var candidates = index.Callables(name).Where(c => c.Path != callerPath || qualifier is not null).ToList();
        if (candidates.Count == 0) return null;
        var exact = candidates.Where(c => c.Symbol.EndsWith(name, StringComparison.Ordinal)).ToList();
        if (exact.Count > 0) candidates = exact;
        if (candidates.Count == 1) return candidates[0];
        if (qualifier is not null)
        {
            var named = candidates.Where(c => Path.GetFileNameWithoutExtension(c.Path).Contains(qualifier, StringComparison.OrdinalIgnoreCase)
                                              || c.Symbol.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count == 1) return named[0];
        }
        // Several declarations: the one in the caller's own folder, when exactly one is there.
        var folder = Path.GetDirectoryName(callerPath)?.Replace('\\', '/') ?? string.Empty;
        var near = candidates.Where(c => (Path.GetDirectoryName(c.Path)?.Replace('\\', '/') ?? string.Empty) == folder).ToList();
        return near.Count == 1 ? near[0] : null;
    }

    private static SymbolLocation? InFile(string path, FileOutline outline, string name)
    {
        foreach (var type in outline.Types)
        {
            if (type.Members.FirstOrDefault(m => m.Name == name) is { } member)
                return new SymbolLocation(path, $"{type.Name}.{member.Name}", member.Line);
        }
        return outline.Functions.FirstOrDefault(f => f.Name == name) is { } function ? new SymbolLocation(path, function.Name, function.Line) : null;
    }

    // "/Transaction/KlaimPending/InitiateData" → KlaimPendingController.InitiateData (the last two segments, so an area
    // in front does not matter); "/Home" → HomeController.Index; "/mod/assign/view.php" → that file.
    private static SymbolLocation? ResolveUrl(string target, StructureSymbolIndex index)
    {
        var url = HttpMethod().Replace(target.Trim().Trim('"', '\'', '`'), string.Empty);
        url = Scheme().Replace(url, string.Empty);
        var cut = url.IndexOfAny(['?', '#']);
        if (cut >= 0) url = url[..cut];
        url = url.TrimStart('~').Trim('/');
        if (url.Length == 0) return null;

        if (ScriptExtensions.Any(e => url.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            var file = index.Files.Where(f => f.Equals(url, StringComparison.OrdinalIgnoreCase) || f.EndsWith("/" + url, StringComparison.OrdinalIgnoreCase)).ToList();
            return file.Count == 1 ? new SymbolLocation(file[0], Path.GetFileName(file[0]), 1) : null;
        }

        var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith('{') && !s.All(char.IsDigit)).ToList();
        var tries = new List<(string Controller, string Action)>();
        if (segments.Count >= 2) tries.Add((segments[^2], segments[^1]));
        if (segments.Count >= 1) tries.Add((segments[^1], "Index"));
        foreach (var (controller, action) in tries)
        {
            foreach (var (path, type) in index.Types(controller + "Controller"))
            {
                if (type.Members.FirstOrDefault(m => m.Name.Equals(action, StringComparison.OrdinalIgnoreCase)) is { } member)
                    return new SymbolLocation(path, $"{type.Name}.{member.Name}", member.Line);
            }
        }
        return null;
    }

    private static SymbolLocation? ResolveView(string target, StructureSymbolIndex index)
    {
        var name = target.Trim().Trim('"', '\'', '`', '~', '/').Replace('\\', '/');
        if (name.Length == 0) return null;
        var hasExtension = ViewExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        var files = index.Files.Where(f => hasExtension
                ? f.Equals(name, StringComparison.OrdinalIgnoreCase) || f.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
                : ViewExtensions.Any(e => f.EndsWith("/" + name + e, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return files.Count == 1 ? new SymbolLocation(files[0], Path.GetFileName(files[0]), 1) : null;
    }

    // "new clsX().Save(a)" → [clsX, Save]; "$this->repo->get" → [$this, repo, get]; "Ns\\Cls::make" → [Ns, Cls, make].
    private static List<string> Qualified(string target)
    {
        var text = target.Trim();
        for (var previous = string.Empty; previous != text;)
        {
            previous = text;
            text = Arguments().Replace(text, string.Empty);
        }
        text = text.Replace("?.", ".").Replace("->", ".").Replace("::", ".").Replace('\\', '.');
        if (text.StartsWith("new ", StringComparison.Ordinal)) text = text[4..];
        return text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Identifier().Match(p).Value)
            .Where(p => p.Length > 0)
            .ToList();
    }

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Arguments();

    [GeneratedRegex(@"^(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex HttpMethod();

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*://[^/]*", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    [GeneratedRegex(@"\$?[A-Za-z_][\w$]*")]
    private static partial Regex Identifier();
}

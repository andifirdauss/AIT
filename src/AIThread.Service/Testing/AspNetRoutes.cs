using System.Text.RegularExpressions;
using AIThread.Service.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AIThread.Service.Testing;

// One endpoint as the code declares it. Method: GET, POST, … or ANY (no verb attribute). Path: the template with its
// tokens replaced ("/api/v1/Outbound/OutletUnionApi/GetAll", "/User/Edit/{id}"). Conventional: no route attribute
// anywhere, so the path assumes the default {controller}/{action} route.
public sealed record CodeRoute(string Method, string Path, string Handler, string File, int Line, bool Conventional = false)
{
    public const string AnyMethod = "ANY";

    public override string ToString() => $"{Method} {Path}";
}

// The endpoints of an ASP.NET (Core MVC / Web API, or Web API 2) project from Roslyn syntax only, so the tester calls
// the path the code really serves instead of one guessed from the class name: [controller] is the class name without
// "Controller" (OutletUnionApiController → OutletUnionApi), [action] the method name ([ActionName], "Async" dropped),
// [area] the [Area], {version:apiVersion} the [ApiVersion] ("1.0" → "1"). Templates starting with "/" or "~/" are not
// combined with the controller's. A class without a route of its own takes its base class's (across files).
public static partial class AspNetRoutes
{
    public const int MaxRoutes = 2_000;
    private const int MaxFileBytes = 512 * 1024;

    // Similarity of the closest route a wrong path is taken for: higher than any other route by a margin.
    private const double CorrectionScore = 0.9;
    private const double HintScore = 0.6;

    private static readonly HashSet<string> Verbs = new(StringComparer.OrdinalIgnoreCase) { "Get", "Post", "Put", "Patch", "Delete", "Head", "Options" };

    public static IReadOnlyList<CodeRoute> Scan(WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var classes = new List<ControllerClass>();
        foreach (var path in scope.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || scope.IsProtected(path)) continue;
            string content;
            try
            {
                if (new FileInfo(path).Length > MaxFileBytes) continue;
                content = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!content.Contains("Controller", StringComparison.Ordinal) && !content.Contains("[Route", StringComparison.Ordinal)) continue;
            classes.AddRange(Classes(content, scope.ToRelative(path)));
        }
        return Resolve(classes);
    }

    // One file on its own: a base class in another file is not seen.
    public static IReadOnlyList<CodeRoute> Parse(string content, string file) => Resolve(Classes(content, file).ToList());

    // The routes a request reaches: the method fits (or the route takes any) and every segment matches.
    public static IReadOnlyList<CodeRoute> Matching(string method, string path, IReadOnlyList<CodeRoute> routes) =>
        routes.Where(r => MethodFits(r, method) && Matches(Segments(r.Path), Segments(path))).ToList();

    // The routes that look most like a path no route serves, best first: a segment spelled a little differently
    // (OutletUnion for OutletUnionApi, v1.0 for v1), one too many or too few.
    public static IReadOnlyList<CodeRoute> Closest(string method, string path, IReadOnlyList<CodeRoute> routes, int max = 3)
    {
        var target = Segments(path);
        if (target.Count == 0) return [];
        return routes
            .Select(r => (Route: r, Score: Similarity(Segments(r.Path), target) - (MethodFits(r, method) ? 0 : 0.2)))
            .Where(x => x.Score >= HintScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Route.Path.Length)
            .Take(max)
            .Select(x => x.Route)
            .ToList();
    }

    // The route a planned target is clearly meant for when no route serves it: same method, the same segments but one
    // or two spelled a little differently, and no other route comes as close. Path: the target with the route's literal
    // segments and its own values where the route has a parameter. Null: it matches, or nothing is clear.
    public static (CodeRoute Route, string Path)? Correction(string method, string path, IReadOnlyList<CodeRoute> routes)
    {
        if (routes.Count == 0 || Matching(method, path, routes).Count > 0) return null;
        var target = Segments(path);
        var ranked = routes
            .Where(r => MethodFits(r, method))
            .Select(r => (Route: r, Segments: Segments(r.Path)))
            .Where(x => x.Segments.Count == target.Count)
            .Select(x => (x.Route, x.Segments, Score: Similarity(x.Segments, target)))
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToList();
        if (ranked.Count == 0 || ranked[0].Score < CorrectionScore) return null;
        if (ranked.Count > 1 && ranked[1].Score >= ranked[0].Score - 0.05) return null;
        var best = ranked[0];
        return (best.Route, "/" + string.Join('/', best.Segments.Select((s, i) => IsParameter(s) && !IsParameter(target[i]) ? target[i] : s)));
    }

    // "POST /api/users?x=1" → ("POST", "/api/users"); a bare path is GET. Null for text that is not a path.
    public static (string Method, string Path)? SplitTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var text = target.Trim();
        var method = "GET";
        if (text.Split(' ', 2, StringSplitOptions.TrimEntries) is [var verb, var rest] && Verbs.Contains(verb))
        {
            method = verb.ToUpperInvariant();
            text = rest;
        }
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            text = absolute.AbsolutePath;
        var query = text.IndexOfAny(['?', '#']);
        if (query >= 0) text = text[..query];
        return text.StartsWith('/') ? (method, text) : null;
    }

    private static bool MethodFits(CodeRoute route, string method) =>
        route.Method == CodeRoute.AnyMethod || route.Method.Equals(method, StringComparison.OrdinalIgnoreCase);

    private static List<string> Segments(string path)
    {
        var query = path.IndexOfAny(['?', '#']);
        if (query >= 0) path = path[..query];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
    }

    private static bool IsParameter(string segment) => segment.StartsWith('{') && segment.EndsWith('}');

    private static bool Matches(List<string> template, List<string> path)
    {
        for (var i = 0; i < template.Count; i++)
        {
            var segment = template[i];
            if (segment.StartsWith("{*", StringComparison.Ordinal)) return true;
            if (i >= path.Count) return template.Skip(i).All(s => IsParameter(s) && s.EndsWith("?}", StringComparison.Ordinal));
            if (IsParameter(segment)) continue;
            if (!segment.Equals(path[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return template.Count == path.Count;
    }

    // 1 for the same segments, less for each one that differs: compared position by position when both have as many,
    // otherwise each segment of the path against the closest of the route's.
    private static double Similarity(List<string> route, List<string> path)
    {
        if (route.Count == 0 || path.Count == 0) return 0;
        if (route.Count == path.Count)
            return route.Zip(path, (r, p) => IsParameter(r) || IsParameter(p) ? 1 : SegmentSimilarity(r, p)).Sum() / route.Count;
        var literals = route.Where(s => !IsParameter(s)).ToList();
        var total = path.Sum(p => literals.Count == 0 ? 0 : literals.Max(r => SegmentSimilarity(r, p)));
        return total / ((route.Count + path.Count) / 2.0);
    }

    private static double SegmentSimilarity(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return 1;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 2 && longer.Contains(shorter, StringComparison.OrdinalIgnoreCase) ? 0.75 : 0;
    }

    private sealed record ActionRoute(string? Method, string? Template);

    private sealed record ControllerAction(string Name, int Line, List<ActionRoute> Routes, List<string> Versions);

    private sealed record ControllerClass(
        string Name, string? BaseName, string File, bool IsAbstract, bool IsMarked, List<string> Templates, string? Area,
        List<string> Versions, List<ControllerAction> Actions)
    {
        // Abstract bases and plain classes only lend their route; controllers are named so or marked [ApiController].
        public bool IsController => !IsAbstract && (IsMarked || Name.EndsWith("Controller", StringComparison.Ordinal));
    }

    private static IEnumerable<ControllerClass> Classes(string content, string file)
    {
        var root = CSharpSyntaxTree.ParseText(content).GetRoot();
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var attributes = Attributes(type.AttributeLists).ToList();
            var templates = attributes.Where(a => a.Name is "Route" or "RoutePrefix").Select(a => a.Template).OfType<string>().ToList();
            var actions = new List<ControllerAction>();
            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) || method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) continue;
                var own = Attributes(method.AttributeLists).ToList();
                if (own.Any(a => a.Name == "NonAction")) continue;
                var name = own.FirstOrDefault(a => a.Name == "ActionName")?.Template
                           ?? (method.Identifier.ValueText.EndsWith("Async", StringComparison.Ordinal) && method.Identifier.ValueText.Length > 5
                               ? method.Identifier.ValueText[..^5]
                               : method.Identifier.ValueText);
                actions.Add(new ControllerAction(name, method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    ActionRoutes(own), own.Where(a => a.Name == "MapToApiVersion").SelectMany(a => a.Values).ToList()));
            }
            yield return new ControllerClass(
                type.Identifier.ValueText,
                type.BaseList?.Types.FirstOrDefault()?.Type is { } baseType ? SimpleName(baseType.ToString()) : null,
                file,
                type.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                attributes.Any(a => a.Name is "ApiController" or "Controller"),
                templates,
                attributes.FirstOrDefault(a => a.Name == "Area")?.Template,
                attributes.Where(a => a.Name == "ApiVersion").SelectMany(a => a.Values).ToList(),
                actions);
        }
    }

    // [HttpPost("GetAll")] → POST GetAll; [HttpPost] + [Route("x")] → POST x; [AcceptVerbs("GET", "POST")] → both.
    private static List<ActionRoute> ActionRoutes(List<ParsedAttribute> attributes)
    {
        var routes = new List<ActionRoute>();
        var plainVerbs = new List<string>();
        foreach (var attribute in attributes)
        {
            if (attribute.Name.StartsWith("Http", StringComparison.Ordinal) && Verbs.Contains(attribute.Name[4..]))
            {
                var verb = attribute.Name[4..].ToUpperInvariant();
                if (attribute.Template is { } template) routes.Add(new ActionRoute(verb, template));
                else plainVerbs.Add(verb);
            }
            else if (attribute.Name == "AcceptVerbs")
            {
                plainVerbs.AddRange(attribute.Values.Where(Verbs.Contains).Select(v => v.ToUpperInvariant()));
            }
        }
        var routeTemplates = attributes.Where(a => a.Name == "Route").Select(a => a.Template).OfType<string>().ToList();
        IEnumerable<string?> verbs = plainVerbs.Count > 0 ? plainVerbs : new string?[] { null };
        foreach (var template in routeTemplates)
            routes.AddRange(verbs.Select(v => new ActionRoute(v, template)));
        if (routes.Count == 0) routes.AddRange(verbs.Select(v => new ActionRoute(v, null)));
        return routes;
    }

    private static List<CodeRoute> Resolve(List<ControllerClass> classes)
    {
        var byName = classes.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var routes = new List<CodeRoute>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in classes.Where(c => c.IsController))
        {
            var templates = Inherited(controller, byName, c => c.Templates.Count > 0 ? c.Templates : null) ?? [];
            var area = Inherited(controller, byName, c => c.Area);
            var classVersions = Inherited(controller, byName, c => c.Versions.Count > 0 ? c.Versions : null) ?? [];
            var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal) && controller.Name.Length > 10 ? controller.Name[..^10] : controller.Name;
            foreach (var action in controller.Actions)
            {
                IEnumerable<string?> versions = action.Versions.Count > 0 ? action.Versions : classVersions.Count > 0 ? classVersions : new string?[] { null };
                foreach (var route in action.Routes)
                {
                    var conventional = templates.Count == 0 && route.Template is null;
                    List<string> paths = conventional
                        ? [area is null ? $"{name}/{action.Name}" : $"{area}/{name}/{action.Name}"]
                        : Combine(templates, route.Template);
                    foreach (var raw in paths)
                    foreach (var version in versions)
                    {
                        var path = Normalize(Replace(raw, name, action.Name, area, version));
                        var method = route.Method ?? CodeRoute.AnyMethod;
                        if (!seen.Add($"{method} {path}")) continue;
                        routes.Add(new CodeRoute(method, path, $"{controller.Name}.{action.Name}", controller.File, action.Line, conventional));
                        if (routes.Count >= MaxRoutes) return routes;
                    }
                }
            }
        }
        return routes;
    }

    // The value of the nearest class up the base chain that has one.
    private static T? Inherited<T>(ControllerClass controller, Dictionary<string, ControllerClass> byName, Func<ControllerClass, T?> pick) where T : class
    {
        var current = controller;
        for (var depth = 0; current is not null && depth < 8; depth++)
        {
            if (pick(current) is { } value) return value;
            current = current.BaseName is { } baseName && byName.TryGetValue(baseName, out var parent) && parent != current ? parent : null;
        }
        return null;
    }

    private static List<string> Combine(List<string> classTemplates, string? actionTemplate)
    {
        if (actionTemplate is not null && (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal)))
            return [actionTemplate.TrimStart('~')];
        if (classTemplates.Count == 0) return [actionTemplate ?? string.Empty];
        return classTemplates.Select(t => actionTemplate is null or "" ? t : $"{t.TrimEnd('/')}/{actionTemplate}").ToList();
    }

    private static string Replace(string template, string controller, string action, string? area, string? version)
    {
        var text = template
            .Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", action, StringComparison.OrdinalIgnoreCase);
        if (area is not null) text = text.Replace("[area]", area, StringComparison.OrdinalIgnoreCase);
        return Parameter().Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            var constraint = m.Groups["constraint"].Value;
            // Reserved route values: {controller} and {action} only match the action's own names.
            if (name.Equals("controller", StringComparison.OrdinalIgnoreCase)) return controller;
            if (name.Equals("action", StringComparison.OrdinalIgnoreCase)) return action;
            if (area is not null && name.Equals("area", StringComparison.OrdinalIgnoreCase)) return area;
            if (version is not null && constraint.Contains("apiVersion", StringComparison.OrdinalIgnoreCase)) return ShortVersion(version);
            return $"{{{m.Groups["star"].Value}{name}{m.Groups["optional"].Value}}}";
        });
    }

    // "1.0" → "1", as Swagger shows a URL-segment version; "2.1" stays.
    private static string ShortVersion(string version) => version.EndsWith(".0", StringComparison.Ordinal) ? version[..^2] : version;

    private static string Normalize(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments);
    }

    private sealed record ParsedAttribute(string Name, string? Template, List<string> Values);

    // Name without namespace and "Attribute"; Template: the first string argument or Template = "…"; Values: every string
    // argument ([ApiVersion("1.0")], [AcceptVerbs("GET", "POST")]).
    private static IEnumerable<ParsedAttribute> Attributes(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var attribute in lists.SelectMany(l => l.Attributes))
        {
            var name = SimpleName(attribute.Name.ToString());
            if (name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > 9) name = name[..^9];
            var arguments = attribute.ArgumentList?.Arguments ?? default;
            var values = arguments
                .Where(a => a.NameEquals is null && a.NameColon is null)
                .Select(a => a.Expression)
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression) || l.IsKind(SyntaxKind.NumericLiteralExpression))
                .Select(l => l.Token.ValueText)
                .ToList();
            var template = arguments
                .FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "Template")?.Expression is LiteralExpressionSyntax named
                ? named.Token.ValueText
                : arguments.FirstOrDefault(a => a.NameEquals is null && a.NameColon is null)?.Expression is LiteralExpressionSyntax first && first.IsKind(SyntaxKind.StringLiteralExpression)
                    ? first.Token.ValueText
                    : null;
            yield return new ParsedAttribute(name, template, values);
        }
    }

    private static string SimpleName(string name)
    {
        var generic = name.IndexOf('<');
        if (generic >= 0) name = name[..generic];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    // {name}, {name?}, {*rest}, {id:int}, {version:apiVersion}, {page=1}.
    [GeneratedRegex(@"\{(?<star>\*{0,2})(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<constraint>(:[^}=?]+)*)(=[^}?]*)?(?<optional>\?)?\}")]
    private static partial Regex Parameter();
}

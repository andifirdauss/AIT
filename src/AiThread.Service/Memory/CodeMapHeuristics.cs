using System.Text.RegularExpressions;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Models;

namespace AiThread.Service.Memory;

// Deterministic first guess of a file's layer and module, from its name, folders and extension. A guess is "sure"
// only when the rule is unambiguous; everything else goes to the model (CodeMapper).
public static partial class CodeMapHeuristics
{
    private static readonly HashSet<string> UiExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cshtml", ".razor", ".html", ".htm", ".css", ".scss", ".sass", ".less", ".vue", ".svelte", ".aspx", ".ascx",
        ".master", ".xaml", ".jsx", ".tsx"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".js", ".mjs", ".cjs", ".ts", ".py", ".java", ".kt", ".go", ".rb", ".php", ".sql", ".swift",
        ".dart", ".rs", ".scala", ".ex", ".exs"
    };

    private static readonly HashSet<string> InfraFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "program.cs", "startup.cs", "dependencyinjection.cs", "globalusings.cs", "dockerfile", "docker-compose.yml",
        "docker-compose.yaml", "package.json", "tsconfig.json", "web.config", "app.config", "global.asax", "makefile",
        "pom.xml", "build.gradle", "settings.gradle", "go.mod", "cargo.toml", "pyproject.toml", "requirements.txt",
        "composer.json", "gemfile", "nuget.config", "directory.build.props", "directory.packages.props"
    };

    private static readonly string[] InfraPrefixes = ["appsettings", "vite.config", "webpack.config", "jest.config", "babel.config", "eslint", ".eslintrc", "next.config", "nuxt.config", "angular.json", "launchsettings"];

    private static readonly HashSet<string> InfraExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".vbproj", ".fsproj", ".sln", ".slnx", ".props", ".targets", ".ps1", ".sh", ".bat", ".cmd" };

    private static readonly HashSet<string> UiFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "views", "pages", "components", "layouts", "wwwroot", "public", "styles", "css", "templates", "partials", "screens", "widgets", "ui"
    };

    private static readonly HashSet<string> EntryFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "controllers", "routes", "endpoints", "handlers", "commands", "consumers", "jobs", "functions", "hubs", "cli"
    };

    private static readonly HashSet<string> DataFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "migrations", "entities", "dtos", "dto", "models", "repositories", "repository", "data", "schema", "schemas", "dal", "persistence", "database", "db", "sql"
    };

    private static readonly HashSet<string> LogicFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "services", "service", "bl", "bll", "business", "domain", "usecases", "store", "stores", "hooks", "validators", "managers", "rules", "core"
    };

    private static readonly HashSet<string> TestFolders = new(StringComparer.OrdinalIgnoreCase) { "test", "tests", "__tests__", "spec", "specs", "e2e" };

    // Suffix → layer, longest first so "ViewModel" wins over "Model".
    private static readonly (string Suffix, MemoryLayer Layer)[] Suffixes =
    [
        ("Controller", MemoryLayer.Entry), ("Endpoints", MemoryLayer.Entry), ("Endpoint", MemoryLayer.Entry), ("Handler", MemoryLayer.Entry),
        ("Consumer", MemoryLayer.Entry), ("Command", MemoryLayer.Entry), ("Function", MemoryLayer.Entry), ("Router", MemoryLayer.Entry),
        ("Routes", MemoryLayer.Entry), ("Hub", MemoryLayer.Entry), ("Job", MemoryLayer.Entry), ("Worker", MemoryLayer.Entry),
        ("Repository", MemoryLayer.Data), ("Repo", MemoryLayer.Data), ("Dao", MemoryLayer.Data), ("DbContext", MemoryLayer.Data),
        ("ViewModel", MemoryLayer.Data), ("Entity", MemoryLayer.Data), ("Dto", MemoryLayer.Data), ("DTO", MemoryLayer.Data),
        ("Request", MemoryLayer.Data), ("Response", MemoryLayer.Data), ("Record", MemoryLayer.Data), ("Mapping", MemoryLayer.Data),
        ("Migration", MemoryLayer.Data), ("Model", MemoryLayer.Data), ("Schema", MemoryLayer.Data),
        ("Service", MemoryLayer.Logic), ("Manager", MemoryLayer.Logic), ("Validator", MemoryLayer.Logic), ("Engine", MemoryLayer.Logic),
        ("Processor", MemoryLayer.Logic), ("Calculator", MemoryLayer.Logic), ("Policy", MemoryLayer.Logic), ("Store", MemoryLayer.Logic),
        ("Slice", MemoryLayer.Logic), ("Client", MemoryLayer.Logic), ("Provider", MemoryLayer.Logic), ("UseCase", MemoryLayer.Logic),
        ("Agent", MemoryLayer.Logic), ("Bll", MemoryLayer.Logic), ("BL", MemoryLayer.Logic), ("Helper", MemoryLayer.Logic),
        ("Page", MemoryLayer.UI), ("View", MemoryLayer.UI), ("Component", MemoryLayer.UI), ("Layout", MemoryLayer.UI),
        ("Tests", MemoryLayer.Test), ("Test", MemoryLayer.Test), ("Spec", MemoryLayer.Test)
    ];

    // Role words stripped from a file name to get its module stem ("UserController" → "User").
    private static readonly string[] StemSuffixes =
    [
        "Controller", "Controllers", "ServiceImpl", "Service", "Services", "Impl", "Repository", "Repo", "Dao", "DbContext",
        "ViewModel", "Dto", "DTO", "Vm", "Model", "Models", "Entity", "Manager", "Handler", "Validator", "Mapper", "Mapping",
        "Configuration", "Config", "Client", "Provider", "Store", "Slice", "Page", "View", "Component", "Form", "List",
        "Details", "Detail", "Api", "Endpoints", "Endpoint", "Hub", "Job", "Consumer", "Command", "Query", "Request",
        "Response", "Tests", "Test", "Spec", "Helper", "Helpers", "Extensions", "Bll", "BL", "Worker", "Agent", "Engine",
        "Processor", "Policy", "Router", "Routes", "Schema", "Migration", "Hook"
    ];

    // Names that say nothing about the feature: the folder above names it ("Views/User/Index.cshtml" → User).
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "index", "main", "app", "default", "form", "list", "edit", "create", "details", "detail", "delete", "view", "page",
        "layout", "_layout", "site", "styles", "style", "global", "common", "utils", "util", "helpers", "constants", "types",
        "routes", "module", "init", "setup", "base", "shared", "_viewimports", "_viewstart", "error", "home"
    };

    // Folders that never name a feature themselves.
    private static readonly HashSet<string> NeutralFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "app", "apps", "lib", "views", "pages", "components", "controllers", "services", "models", "shared", "common",
        "wwwroot", "js", "css", "scripts", "styles", "public", "static", "assets", "client", "server", "api", "web", "core",
        "test", "tests", "spec", "data", "domain", "infrastructure", "application", "features", "modules"
    };

    public static bool IsMappable(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath);
        if (name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) return false;
        return UiExtensions.Contains(extension) || CodeExtensions.Contains(extension) || IsInfra(relativePath);
    }

    // The layer when a rule decides it; null when the path does not tell.
    public static MemoryLayer? Layer(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var name = Path.GetFileName(path);
        var stem = FileStem(name);
        var extension = Path.GetExtension(name);
        var folders = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1];

        if (folders.Any(TestFolders.Contains) || name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase) || stem.EndsWith("Tests", StringComparison.Ordinal))
            return MemoryLayer.Test;
        if (IsInfra(path)) return MemoryLayer.Infra;
        if (folders.Any(f => f.Equals("Migrations", StringComparison.OrdinalIgnoreCase)) || extension.Equals(".sql", StringComparison.OrdinalIgnoreCase))
            return MemoryLayer.Data;

        foreach (var (suffix, layer) in Suffixes)
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal)) return layer;

        // "user.service.ts", "user-store.js": the role is a dotted or dashed part.
        var roleParts = name.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        foreach (var part in roleParts)
            if (MemoryLabels.TryParseLayer(part, out var byPart)) return byPart;

        if (UiExtensions.Contains(extension)) return MemoryLayer.UI;

        // The nearest folder that says something decides.
        for (var i = folders.Length - 1; i >= 0; i--)
        {
            var folder = folders[i];
            if (UiFolders.Contains(folder)) return MemoryLayer.UI;
            if (EntryFolders.Contains(folder)) return MemoryLayer.Entry;
            if (DataFolders.Contains(folder)) return MemoryLayer.Data;
            if (LogicFolders.Contains(folder)) return MemoryLayer.Logic;
        }
        return null;
    }

    // The feature name in a file's path, PascalCase; null when the path does not name one (generic or shared files).
    public static string? ModuleStem(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var name = Path.GetFileName(path);
        var stem = FileStem(name);

        // Interfaces: IUserService → UserService.
        if (stem.Length > 2 && stem[0] == 'I' && char.IsUpper(stem[1]) && char.IsLower(stem.ElementAtOrDefault(2))) stem = stem[1..];

        var stripped = Strip(stem);
        if (stripped.Length < 2 || GenericNames.Contains(stripped) || GenericNames.Contains(stem))
        {
            var folders = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1];
            var folder = folders.Reverse().FirstOrDefault(f => !NeutralFolders.Contains(f) && !UiFolders.Contains(f) && !EntryFolders.Contains(f)
                                                               && !DataFolders.Contains(f) && !LogicFolders.Contains(f) && !TestFolders.Contains(f)
                                                               && !f.StartsWith('.'));
            if (folder is null) return null;
            stripped = Strip(folder);
            if (stripped.Length < 2 || GenericNames.Contains(stripped)) return null;
        }
        return MemoryLabels.Module(stripped);
    }

    // Files and identifiers a prompt refers to: paths with a known extension, and PascalCase names with a hump
    // ("UserController", "UserBL"). Identifiers are resolved to files by CodeMapper.
    public static (IReadOnlyList<string> Paths, IReadOnlyList<string> Identifiers) References(string? text, int max = 20)
    {
        if (string.IsNullOrWhiteSpace(text)) return ([], []);
        var paths = PathPattern().Matches(text).Select(m => m.Value.Trim('`', '\'', '"', '(', ')', ',', '.')).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
        var identifiers = IdentifierPattern().Matches(text)
            .Select(m => m.Value)
            .Where(v => v.Length >= 4 && MemoryLabels.IsIdentifier(v))
            .Distinct(StringComparer.Ordinal)
            .Take(max)
            .ToList();
        return (paths, identifiers);
    }

    public static string FileStem(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var dot = name.IndexOf('.', 1);
        return dot > 0 ? name[..dot] : name;
    }

    // The apps of a repository that holds several (manifest folders); empty for a single app, including a .NET
    // solution of several projects or a workspace whose packages share one stack under one aggregator.
    public static IReadOnlyList<string> AreaFolders(ProjectContext project)
    {
        var manifests = project.Modules.Select(m => m.Replace('\\', '/')).ToList();
        var folders = manifests
            .Where(m => !m.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && !m.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Select(m => Path.GetDirectoryName(m)?.Replace('\\', '/') ?? string.Empty)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folders.Count < 2) return [];

        var hasSolution = manifests.Any(m => m.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || m.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
        var multiStack = project.Stacks.Count > 1;
        var appContainers = folders.Count(f => f.Split('/') is [var top, _, ..] && top.ToLowerInvariant() is "apps" or "packages" or "services" or "projects");
        if (!multiStack && (hasSolution || appContainers < 2)) return [];
        return folders.OrderByDescending(f => f.Length).ToList();
    }

    // The area of a file: the deepest manifest folder above it, named by its last segment without the prefix all
    // areas share ("AiThread.UI" next to "AiThread.API" → "UI").
    public static string? AreaOf(string relativePath, IReadOnlyList<string> areaFolders)
    {
        if (areaFolders.Count == 0) return null;
        var path = relativePath.Replace('\\', '/');
        var folder = areaFolders.FirstOrDefault(f => path.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
        if (folder is null) return null;

        var names = areaFolders.Select(f => f.Split('/')[^1]).ToList();
        var prefix = CommonPrefix(names);
        var name = folder.Split('/')[^1];
        if (prefix.Length > 0 && name.Length > prefix.Length) name = name[prefix.Length..];
        return MemoryLabels.Area(name);
    }

    private static bool IsInfra(string path)
    {
        var name = Path.GetFileName(path);
        if (InfraFiles.Contains(name) || InfraExtensions.Contains(Path.GetExtension(name))) return true;
        if (InfraPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        var normalized = path.Replace('\\', '/');
        return normalized.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/Hosting/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("Hosting/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Strip(string stem)
    {
        // "user-profile.component" → "user-profile"; separators inside the name stay for PascalCase later.
        var current = (stem.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? stem).Trim('_', '-');
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var suffix in StemSuffixes)
            {
                if (current.Length > suffix.Length + 1 && current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    current = current[..^suffix.Length];
                    changed = true;
                    break;
                }
            }
        }
        return current;
    }

    private static string CommonPrefix(IReadOnlyList<string> names)
    {
        if (names.Count < 2) return string.Empty;
        var prefix = names[0];
        foreach (var name in names.Skip(1))
        {
            var length = 0;
            while (length < prefix.Length && length < name.Length && char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(name[length])) length++;
            prefix = prefix[..length];
        }
        // Cut at a separator so a word is never split: "AiThread.UI" and "AiThread.API" share "AiThread.".
        var cut = prefix.LastIndexOfAny(['.', '-', '_']);
        return cut >= 0 ? prefix[..(cut + 1)] : string.Empty;
    }

    [GeneratedRegex(@"(?:[A-Za-z]:)?(?:[\w.\-]+[\\/])*[\w\-]+(?:\.[\w\-]+)*\.(?:cs|cshtml|razor|vb|fs|js|jsx|mjs|ts|tsx|vue|svelte|py|java|kt|go|rb|php|sql|css|scss|less|html|htm|xaml|aspx|ascx|swift|dart|rs)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9]{3,}\b")]
    private static partial Regex IdentifierPattern();
}

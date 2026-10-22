using System.Text.Json;
using System.Text.RegularExpressions;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Testing;

// How AIT starts the app for a test when it is not running yet. Display is how the command is shown to people;
// Environment is added to the inherited one (the URL, the development environment).
public sealed record AppLaunchCommand(
    string Stack,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Display,
    IReadOnlyDictionary<string, string> Environment);

// Picks the command that runs the app of a project, like BuildCommandResolver: fixed per stack and never from a model.
// In order: "run" in .aithread/project.json (committed by the team, for any stack), an ASP.NET Core project
// (dotnet run --urls <baseUrl>), Laravel (php artisan serve) and the "dev"/"start"/"serve" script of package.json.
public static partial class AppLaunchResolver
{
    public const string RunKey = "run";

    public const string Unsupported =
        "AIT belum tahu cara menjalankan project ini (yang dikenali: project ASP.NET Core, Laravel, script \"dev\"/\"start\"/\"serve\" di package.json). " +
        "Tambahkan perintahnya di .aithread/project.json, mis. { \"run\": \"npm run dev\" }, atau jalankan aplikasinya sendiri.";

    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly string[] NodeScripts = ["dev", "start", "serve"];

    // root: the project root; modules: manifest paths relative to it (ProjectContext.Modules); baseUrl: the local URL
    // the plan tests (TestUrls.IsLocal).
    public static AppLaunchCommand? Resolve(string root, IReadOnlyList<string> modules, string baseUrl, out string? error)
    {
        error = null;
        if (!TestUrls.TryParseLocal(baseUrl, out var url))
        {
            error = $"'{baseUrl}' bukan alamat di komputer ini.";
            return null;
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var origin = url.GetLeftPart(UriPartial.Authority);
        var environment = new Dictionary<string, string>
        {
            ["PORT"] = url.Port.ToString(),
            ["ASPNETCORE_URLS"] = origin,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            // Dev servers that open a browser tab on start (create-react-app, some vite configs).
            ["BROWSER"] = "none"
        };

        if (FromOverrideFile(root) is { } run)
        {
            var (program, arguments) = OperatingSystem.IsWindows() ? ("cmd.exe", (IReadOnlyList<string>)["/d", "/s", "/c", run]) : ("/bin/sh", ["-c", run]);
            return new AppLaunchCommand("custom", program, arguments, root, run, environment);
        }

        var ordered = modules.OrderBy(Depth).ThenBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        if (ForDotnet(root, ordered, url, origin, environment, out error) is { } dotnet) return dotnet;
        if (error is not null) return null;

        if (File.Exists(Path.Combine(root, "artisan")))
        {
            return new AppLaunchCommand("PHP (Laravel)", "php", ["artisan", "serve", $"--host={url.Host}", $"--port={url.Port}"], root,
                $"php artisan serve --port={url.Port}", environment);
        }

        foreach (var manifest in ordered.Where(m => string.Equals(Path.GetFileName(m), "package.json", StringComparison.OrdinalIgnoreCase)))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, manifest)))!;
            if (NodeScript(Path.Combine(directory, "package.json")) is not { } script) continue;
            if (!Directory.Exists(Path.Combine(directory, "node_modules")) && !Directory.Exists(Path.Combine(root, "node_modules")))
            {
                error = $"node_modules belum ada di {Where(root, directory)} (jalankan npm install dulu).";
                return null;
            }

            var (program, arguments) = OperatingSystem.IsWindows() ? ("cmd.exe", (IReadOnlyList<string>)["/d", "/c", "npm", "run", script]) : ("npm", ["run", script]);
            return new AppLaunchCommand("Node.js", program, arguments, directory, $"npm run {script}{Suffix(root, directory)}", environment);
        }

        error = Unsupported;
        return null;
    }

    // The ASP.NET Core project the base URL belongs to: the one whose launchSettings.json uses its port, else the only
    // one. Old-format projects run in IIS Express, which AIT does not start.
    private static AppLaunchCommand? ForDotnet(string root, List<string> modules, Uri url, string origin, IReadOnlyDictionary<string, string> environment, out string? error)
    {
        error = null;
        var web = modules
            .Where(m => ProjectExtensions.Contains(Path.GetExtension(m), StringComparer.OrdinalIgnoreCase))
            .Select(m => Path.GetFullPath(Path.Combine(root, m)))
            .Where(IsWebProject)
            .ToList();
        if (web.Count == 0) return null;

        var matching = web.Where(p => UsesPort(p, url.Port)).ToList();
        var project = matching.Count >= 1 ? matching[0] : web.Count == 1 ? web[0] : null;
        if (project is null)
        {
            error = $"Ada {web.Count} project web .NET dan tidak satu pun memakai port {url.Port} di Properties/launchSettings.json, jadi AIT tidak tahu mana yang dijalankan. " +
                    "Tulis perintahnya di .aithread/project.json, mis. { \"run\": \"dotnet run --project src/Web\" }.";
            return null;
        }

        var directory = Path.GetDirectoryName(project)!;
        var relative = Path.GetRelativePath(root, project).Replace('\\', '/');
        return new AppLaunchCommand(".NET", "dotnet", ["run", "--project", project, "--no-launch-profile", "--urls", origin], directory,
            $"dotnet run --project {relative} --urls {origin}", environment);
    }

    private static bool IsWebProject(string project)
    {
        try
        {
            return File.Exists(project) && WebSdk().IsMatch(File.ReadAllText(project)) && !BuildCommandResolver.IsLegacyProject(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool UsesPort(string project, int port)
    {
        var settings = Path.Combine(Path.GetDirectoryName(project)!, "Properties", "launchSettings.json");
        try
        {
            return File.Exists(settings) && Regex.IsMatch(File.ReadAllText(settings), $@"://[^""/;:]+:{port}\b");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? NodeScript(string packageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson), JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("scripts", out var scripts)
                || scripts.ValueKind != JsonValueKind.Object) return null;
            return NodeScripts.FirstOrDefault(s => scripts.TryGetProperty(s, out var value) && value.ValueKind == JsonValueKind.String);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FromOverrideFile(string root)
    {
        var file = Path.Combine(root, ProjectKeyResolver.OverrideFile);
        try
        {
            if (!File.Exists(file)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(file), JsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(RunKey, out var run)
                   && run.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(run.GetString())
                ? run.GetString()!.Trim()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static string Where(string root, string directory) =>
        Path.GetRelativePath(root, directory) is "." ? "root project" : Path.GetRelativePath(root, directory).Replace('\\', '/');

    private static string Suffix(string root, string directory) =>
        Path.GetRelativePath(root, directory) is "." ? string.Empty : $" (di {Path.GetRelativePath(root, directory).Replace('\\', '/')})";

    private static int Depth(string relativePath) => relativePath.Replace('\\', '/').Count(c => c == '/');

    // <Project Sdk="Microsoft.NET.Sdk.Web"> or <Sdk Name="Microsoft.NET.Sdk.Web" />.
    [GeneratedRegex("""Microsoft\.NET\.Sdk\.Web\b""", RegexOptions.IgnoreCase)]
    private static partial Regex WebSdk();
}

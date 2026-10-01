using System.Text.Json;

namespace AiThread.Service.Verification;

// A build AiThread may run. The program and its arguments are fixed per stack and never come from a model: a model
// only chooses which manifest to build, and that must be an existing manifest inside the project root. Root is the
// project root (one build per root at a time); Display is how the command is shown to people and models.
public sealed record BuildCommand(
    string Stack,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Display,
    string Root);

// Picks the build command of a project from its manifests: .NET (dotnet build), Node.js (tsc --noEmit with a local
// TypeScript, else the "build" script), Go (go build), Rust (cargo check) and Maven (compile). Other stacks have no
// build step AiThread can run.
public static class BuildCommandResolver
{
    public const string Unsupported =
        "No supported build command for this project (supported: .NET, Node.js with TypeScript or a \"build\" script, Go, Rust, Maven).";

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    // Default order when no target is given: solutions before project files, then the other stacks; within a kind
    // the shallowest manifest wins.
    private static readonly string[] ManifestNames = ["package.json", "go.mod", "Cargo.toml", "pom.xml"];

    // modules: manifest paths relative to the root (ProjectContext.Modules). target: optional manifest, or folder
    // holding one, relative to the root or absolute inside it.
    public static BuildCommand? Resolve(string root, IReadOnlyList<string> modules, string? target, out string? error)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!string.IsNullOrWhiteSpace(target)) return ForTarget(root, target, out error);

        foreach (var candidate in modules.Where(m => Rank(m) >= 0).OrderBy(Rank).ThenBy(Depth).ThenBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            if (ForManifest(root, Path.GetFullPath(Path.Combine(root, candidate)), out _) is { } command)
            {
                error = null;
                return command;
            }
        }

        error = Unsupported;
        return null;
    }

    private static BuildCommand? ForTarget(string root, string target, out string? error)
    {
        string full;
        try
        {
            var cleaned = target.Trim().Trim('"', '\'', '`').Replace('\\', '/');
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(cleaned) ? cleaned : Path.Combine(root, cleaned)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid build target '{target}'.";
            return null;
        }

        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Build target '{target}' is outside the project root.";
            return null;
        }

        if (Directory.Exists(full))
        {
            var manifest = ManifestIn(full);
            if (manifest is null)
            {
                error = $"No solution, project or build manifest in '{target}'.";
                return null;
            }
            full = manifest;
        }
        else if (!File.Exists(full))
        {
            error = $"Build target '{target}' does not exist.";
            return null;
        }

        var command = ForManifest(root, full, out error);
        if (command is null && error is null) error = $"'{target}' is not a solution, project or build manifest.";
        return command;
    }

    private static BuildCommand? ForManifest(string root, string manifest, out string? error)
    {
        error = null;
        var name = Path.GetFileName(manifest);
        var extension = Path.GetExtension(manifest);
        var directory = Path.GetDirectoryName(manifest)!;
        var relative = Path.GetRelativePath(root, manifest).Replace('\\', '/');
        var relativeDirectory = Path.GetRelativePath(root, directory).Replace('\\', '/');
        var where = relativeDirectory == "." ? string.Empty : $" (in {relativeDirectory})";

        if (Has(SolutionExtensions, extension) || Has(ProjectExtensions, extension))
        {
            // Full paths for the parser; output copies fail fast instead of retrying ten times when a running app
            // locks its own binaries; no vulnerability audit (it can wait on unreachable feeds and adds nothing here).
            return new BuildCommand(".NET", "dotnet",
                ["build", manifest, "-nologo", "-v:q", "-clp:NoSummary", "--tl:off", "-p:GenerateFullPaths=true", "-p:CopyRetryCount=1", "-p:NuGetAudit=false"],
                directory, $"dotnet build {relative}", root);
        }

        switch (name.ToLowerInvariant())
        {
            case "package.json":
                return ForNode(root, directory, where, out error);
            case "go.mod":
                return new BuildCommand("Go", "go", ["build", "./..."], directory, $"go build ./...{where}", root);
            case "cargo.toml":
                return new BuildCommand("Rust", "cargo", ["check", "--quiet", "--message-format", "short"], directory, $"cargo check{where}", root);
            case "pom.xml":
                var wrapper = OperatingSystem.IsWindows() ? "mvnw.cmd" : "mvnw";
                var maven = File.Exists(Path.Combine(directory, wrapper)) ? (OperatingSystem.IsWindows() ? wrapper : "./" + wrapper) : "mvn";
                var (program, arguments) = Shell(maven, "-q", "-B", "compile");
                return new BuildCommand("Java (Maven)", program, arguments, directory, $"{maven} -q -B compile{where}", root);
            default:
                return null;
        }
    }

    // A type check is the quickest complete check; projects without TypeScript fall back to their own build script.
    private static BuildCommand? ForNode(string root, string directory, string where, out string? error)
    {
        error = null;
        if (File.Exists(Path.Combine(directory, "tsconfig.json")) && LocalTypeScript(root, directory) is { } tsc)
        {
            return new BuildCommand("Node.js", "node", [tsc, "--noEmit", "--pretty", "false", "-p", "tsconfig.json"],
                directory, $"tsc --noEmit{where}", root);
        }

        if (HasBuildScript(Path.Combine(directory, "package.json")))
        {
            var (program, arguments) = Shell("npm", "run", "build");
            return new BuildCommand("Node.js", program, arguments, directory, $"npm run build{where}", root);
        }

        error = "package.json has no \"build\" script and the folder has no local TypeScript compiler (npm install first).";
        return null;
    }

    // node_modules/typescript of the folder itself or, in a workspace, of the project root.
    private static string? LocalTypeScript(string root, string directory) =>
        new[] { directory, root }
            .Select(d => Path.Combine(d, "node_modules", "typescript", "bin", "tsc"))
            .FirstOrDefault(File.Exists);

    private static bool HasBuildScript(string packageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("scripts", out var scripts)
                   && scripts.ValueKind == JsonValueKind.Object
                   && scripts.TryGetProperty("build", out var build)
                   && build.ValueKind == JsonValueKind.String;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // npm and mvn are batch files on Windows, which only cmd.exe runs. The arguments are constants, never paths.
    private static (string Program, IReadOnlyList<string> Arguments) Shell(string tool, params string[] arguments) =>
        OperatingSystem.IsWindows() ? ("cmd.exe", ["/d", "/c", tool, .. arguments]) : (tool, arguments);

    private static string? ManifestIn(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            return files.FirstOrDefault(f => Has(SolutionExtensions, Path.GetExtension(f)))
                   ?? files.FirstOrDefault(f => Has(ProjectExtensions, Path.GetExtension(f)))
                   ?? ManifestNames.Select(n => files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int Rank(string manifest)
    {
        var extension = Path.GetExtension(manifest);
        if (Has(SolutionExtensions, extension)) return 0;
        if (Has(ProjectExtensions, extension)) return 1;
        var index = Array.FindIndex(ManifestNames, n => string.Equals(n, Path.GetFileName(manifest), StringComparison.OrdinalIgnoreCase));
        return index < 0 ? -1 : 2 + index;
    }

    private static int Depth(string relativePath) => relativePath.Replace('\\', '/').Count(c => c == '/');

    private static bool Has(string[] values, string value) => values.Contains(value, StringComparer.OrdinalIgnoreCase);
}

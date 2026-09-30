namespace AiThread.Service.Workspace;

// A folder that counts as a project: it holds .git or a build manifest. Stacks come from the manifests directly in it.
public sealed record ProjectRoot(string Path, string Name, IReadOnlyList<string> Stacks, bool IsRepository);

// Root is the project the start folder belongs to. Without one, Nearby lists the project folders just below the start
// folder (a folder holding several projects is ambiguous, so none of them is picked).
public sealed record ProjectResolution(ProjectRoot? Root, IReadOnlyList<ProjectRoot> Nearby);

public sealed record ProjectScanResult(IReadOnlyList<ProjectRoot> Projects, int FoldersScanned, bool Truncated);

public sealed record ManifestFile(string RelativePath, string Stack);

// Decides what a project is without looking at any one ecosystem's files: a folder is a project root when it holds
// .git or a build manifest (.sln, pom.xml, package.json, go.mod, ...). A session belongs to the project it is opened in
// or below; folders without any marker are not projects and get no memory.
public static class ProjectLocator
{
    private const int MaxParentLevels = 6;
    private const int MaxNearbyDepth = 2;
    private const int MaxNearby = 12;
    private const int MaxNearbyFolders = 200;

    private const string DotNet = ".NET";
    private const string Maven = "Java (Maven)";
    private const string Gradle = "JVM (Gradle)";
    private const string Sbt = "Scala (sbt)";
    private const string Node = "Node.js";
    private const string Deno = "Deno";
    private const string Python = "Python";
    private const string Go = "Go";
    private const string Rust = "Rust";
    private const string Php = "PHP";
    private const string Ruby = "Ruby";
    private const string Elixir = "Elixir";
    private const string Dart = "Dart/Flutter";
    private const string CMake = "C/C++ (CMake)";
    private const string Swift = "Swift";

    // Aggregators tie several modules together (a solution, a Gradle settings file, a JS workspace); when no
    // repository root exists, the outermost one is the project root.
    private sealed record Marker(string Stack, bool Aggregator = false);

    private static readonly Dictionary<string, Marker> FileMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pom.xml"] = new(Maven),
        ["build.gradle"] = new(Gradle),
        ["build.gradle.kts"] = new(Gradle),
        ["settings.gradle"] = new(Gradle, true),
        ["settings.gradle.kts"] = new(Gradle, true),
        ["build.sbt"] = new(Sbt),
        ["package.json"] = new(Node),
        ["pnpm-workspace.yaml"] = new(Node, true),
        ["lerna.json"] = new(Node, true),
        ["nx.json"] = new(Node, true),
        ["turbo.json"] = new(Node, true),
        ["deno.json"] = new(Deno),
        ["deno.jsonc"] = new(Deno),
        ["pyproject.toml"] = new(Python),
        ["setup.py"] = new(Python),
        ["requirements.txt"] = new(Python),
        ["Pipfile"] = new(Python),
        ["go.mod"] = new(Go),
        ["go.work"] = new(Go, true),
        ["Cargo.toml"] = new(Rust),
        ["composer.json"] = new(Php),
        ["Gemfile"] = new(Ruby),
        ["mix.exs"] = new(Elixir),
        ["pubspec.yaml"] = new(Dart),
        ["CMakeLists.txt"] = new(CMake),
        ["Package.swift"] = new(Swift)
    };

    private static readonly Dictionary<string, Marker> ExtensionMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        [".sln"] = new(DotNet, true),
        [".slnx"] = new(DotNet, true),
        [".csproj"] = new(DotNet),
        [".fsproj"] = new(DotNet),
        [".vbproj"] = new(DotNet)
    };

    // Build output that is only noise for one stack; folders like "build" or "vendor" are not hidden in general
    // because in other stacks they can hold real source.
    private static readonly Dictionary<string, string[]> StackIgnoredDirectories = new()
    {
        [Maven] = ["target"],
        [Gradle] = ["build", "out"],
        [Sbt] = ["target"],
        [Node] = ["dist", "build", "out", "coverage"],
        [Python] = ["build", "dist", "venv", "site-packages"],
        [Go] = ["vendor"],
        [Php] = ["vendor"],
        [Ruby] = ["vendor"],
        [Rust] = ["target"],
        [Elixir] = ["_build", "deps"],
        [Dart] = ["build"],
        [CMake] = ["build"],
        [Swift] = [".build"]
    };

    private sealed record MarkerHit(string FileName, string Stack, bool Aggregator);

    private sealed record Findings(bool IsRepository, IReadOnlyList<MarkerHit> Hits)
    {
        public bool HasAggregator => Hits.Any(h => h.Aggregator);
    }

    private static readonly Lazy<HashSet<string>> BlockedRoots = new(BuildBlockedRoots);

    // The project a folder belongs to: the nearest repository root above it, else the outermost solution/workspace
    // file, else the nearest manifest. Never looks below the folder; a folder that only holds projects is ambiguous.
    public static ProjectResolution Resolve(string startPath, int maxParentLevels = MaxParentLevels)
    {
        var start = Normalize(startPath);
        if (start is not null && File.Exists(start)) start = Path.GetDirectoryName(start);
        if (start is null || !Directory.Exists(start)) return new ProjectResolution(null, []);

        var chain = new List<(DirectoryInfo Directory, Findings Findings)>();
        var current = new DirectoryInfo(start);
        for (var level = 0; current is not null && level <= maxParentLevels; level++, current = current.Parent)
        {
            if (IsBlockedRoot(current.FullName)) break;
            if (Read(current.FullName) is { } findings) chain.Add((current, findings));
        }

        var pick = chain.FindIndex(c => c.Findings.IsRepository);
        if (pick < 0) pick = chain.FindLastIndex(c => c.Findings.HasAggregator);
        if (pick < 0 && chain.Count > 0) pick = 0;
        if (pick >= 0) return new ProjectResolution(ToRoot(chain[pick].Directory.FullName, chain[pick].Findings), []);

        return new ProjectResolution(null, IsBlockedRoot(start) ? [] : Nearby(new DirectoryInfo(start)));
    }

    // The folder itself as a project root, or null when it holds neither .git nor a manifest.
    public static ProjectRoot? Inspect(string directory) =>
        Normalize(directory) is { } full && Read(full) is { } findings ? ToRoot(full, findings) : null;

    // A folder with no marker registered by hand: still a project, just without stacks.
    public static ProjectRoot Plain(string directory)
    {
        var full = Normalize(directory) ?? directory;
        return new ProjectRoot(full, NameOf(full), [], false);
    }

    // Drive roots, the user's home and well-known personal or system folders are never project roots, even when a
    // stray package.json or .git sits in them; that would make every unrelated session a member of one project.
    public static bool IsBlockedRoot(string path)
    {
        var full = Normalize(path);
        if (full is null) return true;
        return string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase) || BlockedRoots.Value.Contains(full);
    }

    // Manifest files below a root (breadth first, nearest first), as paths relative to it.
    public static IReadOnlyList<ManifestFile> FindManifests(string root, int maxDepth = 3, int maxDirectories = 150)
    {
        var manifests = new List<ManifestFile>();
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists) return manifests;

        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((rootInfo, 0));
        var visited = 0;

        while (pending.Count > 0 && visited < maxDirectories)
        {
            var (directory, depth) = pending.Dequeue();
            visited++;

            if (Read(directory.FullName) is { } findings)
            {
                foreach (var hit in findings.Hits.OrderBy(h => h.FileName, StringComparer.OrdinalIgnoreCase))
                {
                    var relative = Path.GetRelativePath(rootInfo.FullName, Path.Combine(directory.FullName, hit.FileName));
                    manifests.Add(new ManifestFile(relative.Replace('\\', '/'), hit.Stack));
                }
            }

            if (depth >= maxDepth) continue;
            foreach (var child in BrowsableDirectories(directory)) pending.Enqueue((child, depth + 1));
        }

        return manifests;
    }

    public static IReadOnlyList<string> StacksOf(IEnumerable<ManifestFile> manifests) =>
        manifests.Select(m => m.Stack).Distinct().Order(StringComparer.Ordinal).ToList();

    // Extra folders to hide from planning for the stacks of a project (on top of the universal build/VCS folders).
    public static IReadOnlyList<string> IgnoredDirectoriesFor(IEnumerable<string> stacks) =>
        stacks.SelectMany(s => StackIgnoredDirectories.GetValueOrDefault(s) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Breadth-first search for project roots below a folder, nearest first. A project is not searched further (its
    // modules belong to it). Stops at the depth, result or folder limit, or when cancelled (the caller applies a time
    // limit), and reports whether it stopped early.
    public static ProjectScanResult Scan(string root, int maxDepth, int maxResults, int maxFolders, CancellationToken cancellationToken)
    {
        var projects = new List<ProjectRoot>();
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((new DirectoryInfo(root), 0));
        var scanned = 0;

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested || projects.Count >= maxResults || scanned >= maxFolders)
                return new ProjectScanResult(projects, scanned, Truncated: true);

            var (directory, depth) = pending.Dequeue();
            scanned++;

            if (!IsBlockedRoot(directory.FullName) && Read(directory.FullName) is { } findings)
            {
                projects.Add(ToRoot(directory.FullName, findings));
                continue;
            }

            if (depth >= maxDepth) continue;
            foreach (var child in BrowsableDirectories(directory)) pending.Enqueue((child, depth + 1));
        }

        return new ProjectScanResult(projects, scanned, Truncated: false);
    }

    // Sub-folders worth showing when looking for projects: no hidden/system folders, no build output or VCS
    // folders, and no junctions/symlinks (following them could loop). Cloud placeholders (OneDrive) stay visible.
    public static IReadOnlyList<DirectoryInfo> BrowsableDirectories(DirectoryInfo directory)
    {
        DirectoryInfo[] children;
        try
        {
            children = directory.GetDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }

        var result = new List<DirectoryInfo>(children.Length);
        foreach (var child in children)
        {
            try
            {
                if (child.Name.StartsWith('.') || child.Name.StartsWith('$') || WorkspaceScope.IsIgnoredDirectoryName(child.Name)) continue;
                if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (child.LinkTarget is not null) continue;
                result.Add(child);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Unreadable entry — skip it.
            }
        }

        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Project folders one or two levels below a folder that is not a project itself, for the log line that tells the
    // user where to open the session instead.
    private static IReadOnlyList<ProjectRoot> Nearby(DirectoryInfo start)
    {
        var found = new List<ProjectRoot>();
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((start, 0));
        var listed = 0;

        while (pending.Count > 0 && found.Count < MaxNearby && listed < MaxNearbyFolders)
        {
            var (directory, depth) = pending.Dequeue();
            foreach (var child in BrowsableDirectories(directory))
            {
                listed++;
                if (Read(child.FullName) is { } findings)
                {
                    found.Add(ToRoot(child.FullName, findings));
                    if (found.Count >= MaxNearby) break;
                }
                else if (depth + 1 < MaxNearbyDepth)
                {
                    pending.Enqueue((child, depth + 1));
                }
            }
        }

        return found;
    }

    private static Findings? Read(string directory)
    {
        var hits = new List<MarkerHit>();
        var repository = false;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                // A file rather than a folder in worktrees and submodules.
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    repository = true;
                    continue;
                }

                if (FileMarkers.TryGetValue(name, out var marker) || ExtensionMarkers.TryGetValue(Path.GetExtension(name), out marker))
                    hits.Add(new MarkerHit(name, marker.Stack, marker.Aggregator || DeclaresModules(entry, name)));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }

        return repository || hits.Count > 0 ? new Findings(repository, hits) : null;
    }

    // A parent pom lists <modules>, a JS monorepo root declares "workspaces": those manifests tie their sub-projects
    // together like a solution does. Only the start of the file is read.
    private static bool DeclaresModules(string file, string name)
    {
        var token = name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase) ? "<modules>"
            : name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ? "\"workspaces\""
            : null;
        if (token is null) return false;

        try
        {
            using var reader = new StreamReader(file);
            var buffer = new char[64 * 1024];
            var length = reader.Read(buffer, 0, buffer.Length);
            return new string(buffer, 0, length).Contains(token, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ProjectRoot ToRoot(string directory, Findings findings) => new(
        directory,
        NameOf(directory),
        findings.Hits.Select(h => h.Stack).Distinct().Order(StringComparer.Ordinal).ToList(),
        findings.IsRepository);

    private static string NameOf(string directory) => new DirectoryInfo(directory).Name;

    private static string? Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static HashSet<string> BuildBlockedRoots()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Normalize(path) is { } full) blocked.Add(full);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(home);
        if (!string.IsNullOrWhiteSpace(home)) Add(Path.GetDirectoryName(home));
        if (!string.IsNullOrWhiteSpace(home)) Add(Path.Combine(home, "Downloads"));

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                     Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.Windows,
                     Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            Add(Environment.GetFolderPath(folder));
        }

        return blocked;
    }
}

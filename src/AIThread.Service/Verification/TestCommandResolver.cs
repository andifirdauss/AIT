using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIThread.Service.Verification;

// Finds the test suites of a project from its manifests and builds the command that runs a suite, or only some of its
// tests. Supported: .NET (dotnet test with TRX; vstest.console for old-format .NET Framework projects), Node.js (jest,
// vitest, mocha from node_modules; any other "test" script by exit code only), Python (pytest), PHP (PHPUnit), Java
// (Maven surefire, Gradle), Go and Rust. Programs and arguments are constants; test names only reach a runner as a
// filter AIT escapes itself.
public static partial class TestCommandResolver
{
    // Above this the filter is dropped and the whole suite runs: a very long command line fails on Windows.
    private const int MaxFilteredTests = 120;
    private const int MaxFilterChars = 6_000;

    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    // npm init writes this as the test script of a package without tests.
    private const string NpmDefaultTest = "no test specified";

    public static IReadOnlyList<TestSuite> Suites(string root, IReadOnlyList<string> modules)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var suites = new List<TestSuite>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in DotNetProjects(root, modules))
            if (seen.Add(project) && DotNetSuite(root, project) is { } suite) suites.Add(suite);

        foreach (var module in modules)
        {
            var full = Full(root, module);
            if (full is null) continue;
            var directory = Path.GetDirectoryName(full)!;
            var suite = Path.GetFileName(full).ToLowerInvariant() switch
            {
                "package.json" => NodeSuite(root, full),
                "pyproject.toml" or "setup.py" or "requirements.txt" or "pipfile" => PythonSuite(root, directory),
                "composer.json" => PhpSuite(root, directory),
                "pom.xml" => Directory.Exists(Path.Combine(directory, "src", "test")) ? Make(TestSuite.JUnit, "Java (Maven)", root, full, TestReportFormat.JUnitXml) : null,
                "build.gradle" or "build.gradle.kts" => Directory.Exists(Path.Combine(directory, "src", "test")) ? Make(TestSuite.JUnit, "JVM (Gradle)", root, full, TestReportFormat.JUnitXml) : null,
                "go.mod" => HasFile(directory, "*_test.go") ? Make(TestSuite.Go, "Go", root, full, TestReportFormat.GoJson) : null,
                "cargo.toml" => Make(TestSuite.Rust, "Rust", root, full, TestReportFormat.RustText),
                _ => null
            };
            // A Python project has several manifests in one folder; one suite per folder.
            if (suite is not null && seen.Add(suite.Framework + "|" + suite.Directory)) suites.Add(suite);
        }

        return suites.OrderBy(s => s.Manifest, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The suite a test file belongs to: the deepest suite folder that holds it, for the file's language.
    public static TestSuite? SuiteFor(IEnumerable<TestSuite> suites, string root, string testFile)
    {
        var full = Full(root, testFile);
        if (full is null) return null;
        var extension = Path.GetExtension(full).ToLowerInvariant();
        return suites
            .Where(s => Accepts(s, extension) && IsInside(full, s.Directory))
            .OrderByDescending(s => s.Directory.Length)
            .FirstOrDefault();
    }

    // The command for a suite. targets null or empty: the whole suite. reportPath: a fresh file path in a folder of
    // AIT's own (Maven and Gradle ignore it and write below the module).
    public static TestCommand Command(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        var wanted = targets is { Count: > 0 and <= MaxFilteredTests } ? targets : null;
        var command = suite.Framework switch
        {
            _ when suite.IsLegacyDotNet => VsTest(suite, wanted, reportPath),
            TestSuite.XUnit or TestSuite.NUnit or TestSuite.MsTest or TestSuite.DotNet => DotNetTest(suite, wanted, reportPath),
            TestSuite.Jest => Jest(suite, wanted, reportPath),
            TestSuite.Vitest => Vitest(suite, wanted, reportPath),
            TestSuite.Mocha => Mocha(suite, wanted, reportPath),
            TestSuite.Pytest => Pytest(suite, wanted, reportPath),
            TestSuite.PhpUnit => PhpUnit(suite, wanted, reportPath),
            TestSuite.JUnit => suite.Stack.Contains("Maven", StringComparison.Ordinal) ? Maven(suite, wanted) : Gradle(suite, wanted),
            TestSuite.Go => GoTest(suite, wanted),
            TestSuite.Rust => Cargo(suite, wanted),
            _ => NpmTest(suite)
        };
        return command;
    }

    private static TestSuite Make(string framework, string stack, string root, string manifest, TestReportFormat format, string? problem = null) =>
        new(framework, stack, Path.GetRelativePath(root, manifest).Replace('\\', '/'), Path.GetDirectoryName(manifest)!, root, format, problem);

    // --- .NET ---

    // Project files among the manifests plus the projects every solution lists (test projects often sit deeper than
    // the manifest search goes).
    private static IEnumerable<string> DotNetProjects(string root, IReadOnlyList<string> modules)
    {
        foreach (var module in modules)
        {
            var full = Full(root, module);
            if (full is null) continue;
            var extension = Path.GetExtension(full);
            if (Has(ProjectExtensions, extension)) yield return full;
            else if (Has(SolutionExtensions, extension))
                foreach (var project in SolutionProjects(full).Where(p => IsInside(p, root))) yield return project;
        }
    }

    private static IEnumerable<string> SolutionProjects(string solution)
    {
        string text;
        try
        {
            text = File.ReadAllText(solution);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(solution)!;
        foreach (var path in SlnProject().Matches(text).Select(m => m.Groups["path"].Value).Concat(SlnxProject().Matches(text).Select(m => m.Groups["path"].Value)))
        {
            if (!Has(ProjectExtensions, Path.GetExtension(path)) || path.Contains("://", StringComparison.Ordinal)) continue;
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(directory, path.Replace('\\', '/')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (File.Exists(full)) yield return full;
        }
    }

    private static TestSuite? DotNetSuite(string root, string project)
    {
        string text;
        try
        {
            text = File.ReadAllText(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var framework = DotNetFramework(text);
        if (framework is null) return null;

        if (!BuildCommandResolver.IsLegacyProject(project))
            return Make(framework, ".NET", root, project, TestReportFormat.Trx);

        // Old-format projects only build with Visual Studio's MSBuild; their tests run from the built assembly.
        var vstest = VsTestConsole();
        var assembly = BuiltAssembly(project, text);
        var problem = vstest is null ? "vstest.console.exe dari Visual Studio tidak ditemukan di mesin ini."
            : assembly is null ? "Assembly test belum di-build (jalankan build dulu)."
            : null;
        return Make(framework, ".NET Framework", root, project, TestReportFormat.Trx, problem) with { IsLegacyDotNet = true };
    }

    // Null when the project references no test framework.
    private static string? DotNetFramework(string projectText)
    {
        if (projectText.Contains("xunit", StringComparison.OrdinalIgnoreCase)) return TestSuite.XUnit;
        if (projectText.Contains("nunit", StringComparison.OrdinalIgnoreCase)) return TestSuite.NUnit;
        if (projectText.Contains("MSTest", StringComparison.OrdinalIgnoreCase)
            || projectText.Contains("QualityTools.UnitTestFramework", StringComparison.OrdinalIgnoreCase)) return TestSuite.MsTest;
        if (projectText.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
            || IsTestProject().IsMatch(projectText)) return TestSuite.DotNet;
        return null;
    }

    private static TestCommand DotNetTest(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        var project = Path.Combine(suite.Root, suite.Manifest);
        var filter = targets is null ? null : DotNetFilter(targets);
        List<string> arguments = ["test", project, "-nologo", "--logger", "trx", "--results-directory", Path.GetDirectoryName(reportPath)!];
        if (filter is not null) arguments.AddRange(["--filter", filter]);
        return new TestCommand(suite, "dotnet", arguments, $"dotnet test {suite.Manifest}" + (filter is null ? string.Empty : $" ({targets!.Count} test)"),
            Path.GetDirectoryName(reportPath), filter is not null);
    }

    private static TestCommand VsTest(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        var project = Path.Combine(suite.Root, suite.Manifest);
        var assembly = BuiltAssembly(project, SafeRead(project) ?? string.Empty) ?? project;
        var filter = targets is null ? null : DotNetFilter(targets);
        List<string> arguments = [assembly, "/Logger:trx", $"/ResultsDirectory:{Path.GetDirectoryName(reportPath)}"];
        if (filter is not null) arguments.Add($"/TestCaseFilter:{filter}");
        return new TestCommand(suite, VsTestConsole() ?? "vstest.console.exe", arguments,
            $"vstest.console {Path.GetFileName(assembly)}" + (filter is null ? string.Empty : $" ({targets!.Count} test)"), Path.GetDirectoryName(reportPath), filter is not null);
    }

    // FullyQualifiedName=Ns.Class.Method or a data row of it (NUnit adds the arguments to the name).
    private static string? DotNetFilter(IReadOnlyList<TestTarget> targets)
    {
        var parts = targets.Select(t => DotNetEscape(t.Selector)).Distinct(StringComparer.Ordinal)
            .Select(name => $"FullyQualifiedName={name}|FullyQualifiedName~{name}\\(");
        var filter = string.Join("|", parts);
        return filter.Length <= MaxFilterChars ? filter : null;
    }

    private static string DotNetEscape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~') builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    // vstest.console.exe of the Visual Studio whose MSBuild builds the old-format projects.
    private static string? VsTestConsole()
    {
        if (MsBuildLocator.Find() is not { } msBuild) return null;
        // <VS>\MSBuild\Current\Bin\MSBuild.exe → <VS>\Common7\IDE\Extensions\TestPlatform\vstest.console.exe
        var visualStudio = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(msBuild)!, "..", "..", ".."));
        var path = Path.Combine(visualStudio, "Common7", "IDE", "Extensions", "TestPlatform", "vstest.console.exe");
        return File.Exists(path) ? path : null;
    }

    // The newest <AssemblyName>.dll below the project's bin folder.
    private static string? BuiltAssembly(string project, string projectText)
    {
        var name = AssemblyName().Match(projectText) is { Success: true } match ? match.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(project);
        var bin = Path.Combine(Path.GetDirectoryName(project)!, "bin");
        if (!Directory.Exists(bin)) return null;
        try
        {
            return Directory.EnumerateFiles(bin, name + ".dll", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // --- Node.js ---

    private static TestSuite? NodeSuite(string root, string packageJson)
    {
        var directory = Path.GetDirectoryName(packageJson)!;
        string? testScript = null;
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var section in new[] { "dependencies", "devDependencies" })
                if (rootElement.TryGetProperty(section, out var deps) && deps.ValueKind == JsonValueKind.Object)
                    foreach (var dep in deps.EnumerateObject()) dependencies.Add(dep.Name);
            if (rootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object
                && scripts.TryGetProperty("test", out var test) && test.ValueKind == JsonValueKind.String)
                testScript = test.GetString();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var script = testScript ?? string.Empty;
        string? framework = dependencies.Contains("vitest") || script.Contains("vitest", StringComparison.Ordinal) ? TestSuite.Vitest
            : dependencies.Contains("jest") || script.Contains("jest", StringComparison.Ordinal) ? TestSuite.Jest
            : dependencies.Contains("mocha") || script.Contains("mocha", StringComparison.Ordinal) ? TestSuite.Mocha
            : null;
        if (framework is null)
        {
            return testScript is null || testScript.Contains(NpmDefaultTest, StringComparison.OrdinalIgnoreCase)
                ? null
                : Make(TestSuite.Npm, "Node.js", root, packageJson, TestReportFormat.ExitCode);
        }

        var bin = NodeBin(root, directory, framework);
        return Make(framework, "Node.js", root, packageJson, framework == TestSuite.Mocha ? TestReportFormat.MochaJson : TestReportFormat.JestJson,
            bin is null ? $"{framework} tidak ada di node_modules (jalankan npm install dulu)." : null);
    }

    // The runner's own script in node_modules of the package or, in a workspace, of the root; run with node so no
    // shell parses the arguments.
    private static string? NodeBin(string root, string directory, string framework)
    {
        var relative = framework switch
        {
            TestSuite.Jest => Path.Combine("node_modules", "jest", "bin", "jest.js"),
            TestSuite.Vitest => Path.Combine("node_modules", "vitest", "vitest.mjs"),
            _ => Path.Combine("node_modules", "mocha", "bin", "mocha.js")
        };
        return new[] { directory, root }.Select(d => Path.Combine(d, relative)).FirstOrDefault(File.Exists);
    }

    private static TestCommand Jest(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        List<string> arguments = [NodeBin(suite.Root, suite.Directory, TestSuite.Jest)!, "--ci", "--json", $"--outputFile={reportPath}"];
        if (targets is not null)
        {
            arguments.Add("--runTestsByPath");
            arguments.AddRange(targets.Select(t => Path.Combine(suite.Root, t.TestFile)).Distinct(StringComparer.OrdinalIgnoreCase));
            arguments.AddRange(["-t", TitlePattern(targets)]);
        }
        return new TestCommand(suite, "node", arguments, $"jest{Where(suite)}" + Count(targets), reportPath, targets is not null);
    }

    private static TestCommand Vitest(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        List<string> arguments = [NodeBin(suite.Root, suite.Directory, TestSuite.Vitest)!, "run", "--reporter=json", $"--outputFile={reportPath}"];
        if (targets is not null)
        {
            arguments.AddRange(targets.Select(t => Path.GetRelativePath(suite.Directory, Path.Combine(suite.Root, t.TestFile)).Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase));
            arguments.AddRange(["-t", TitlePattern(targets)]);
        }
        return new TestCommand(suite, "node", arguments, $"vitest run{Where(suite)}" + Count(targets), reportPath, targets is not null);
    }

    private static TestCommand Mocha(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        List<string> arguments = [NodeBin(suite.Root, suite.Directory, TestSuite.Mocha)!, "--reporter", "json", "--reporter-option", $"output={reportPath}"];
        if (targets is not null)
        {
            arguments.AddRange(targets.Select(t => Path.Combine(suite.Root, t.TestFile)).Distinct(StringComparer.OrdinalIgnoreCase));
            arguments.AddRange(["--grep", TitlePattern(targets)]);
        }
        return new TestCommand(suite, "node", arguments, $"mocha{Where(suite)}" + Count(targets), reportPath, targets is not null);
    }

    private static TestCommand NpmTest(TestSuite suite)
    {
        var (program, arguments) = Shell("npm", "test");
        return new TestCommand(suite, program, arguments, $"npm test{Where(suite)}", null, false);
    }

    // A JavaScript regular expression that matches a test whose full name (describe blocks and title) ends with one of
    // the titles.
    private static string TitlePattern(IReadOnlyList<TestTarget> targets) =>
        "(" + string.Join("|", targets.Select(t => JsEscape(t.MethodName)).Distinct(StringComparer.Ordinal)) + ")$";

    private static string JsEscape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ("\\^$.*+?()[]{}|/".Contains(c)) builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    // --- Python ---

    private static TestSuite? PythonSuite(string root, string directory)
    {
        if (!HasFile(directory, "test_*.py") && !HasFile(directory, "*_test.py")) return null;
        // The folder is the suite's manifest: pytest is configured by several files (pyproject.toml, pytest.ini, ...).
        return new TestSuite(TestSuite.Pytest, "Python", Path.GetRelativePath(root, directory).Replace('\\', '/'), directory, root, TestReportFormat.JUnitXml);
    }

    private static TestCommand Pytest(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        List<string> arguments = ["-m", "pytest", "-q", "-p", "no:cacheprovider", $"--junitxml={reportPath}"];
        if (targets is not null) arguments.AddRange(targets.Select(t => t.Selector).Distinct(StringComparer.Ordinal));
        return new TestCommand(suite, Python(suite.Root, suite.Directory), arguments, $"pytest{Where(suite)}" + Count(targets), reportPath, targets is not null);
    }

    // The project's virtual environment when it has one.
    private static string Python(string root, string directory)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(".venv", "Scripts", "python.exe"), Path.Combine("venv", "Scripts", "python.exe")]
            : [Path.Combine(".venv", "bin", "python"), Path.Combine("venv", "bin", "python")];
        return new[] { directory, root }.SelectMany(d => candidates.Select(c => Path.Combine(d, c))).FirstOrDefault(File.Exists)
               ?? (OperatingSystem.IsWindows() ? "python" : "python3");
    }

    // --- PHP ---

    private static TestSuite? PhpSuite(string root, string directory)
    {
        var config = new[] { "phpunit.xml", "phpunit.xml.dist", "phpunit.dist.xml" }.Any(n => File.Exists(Path.Combine(directory, n)));
        if (!config) return null;
        var phpunit = PhpUnitScript(root, directory);
        return Make(TestSuite.PhpUnit, "PHP", root, Path.Combine(directory, "composer.json"), TestReportFormat.JUnitXml,
            phpunit is null ? "vendor/bin/phpunit tidak ada (jalankan composer install dulu)." : null);
    }

    private static string? PhpUnitScript(string root, string directory) =>
        new[] { directory, root }.Select(d => Path.Combine(d, "vendor", "bin", "phpunit")).FirstOrDefault(File.Exists);

    private static TestCommand PhpUnit(TestSuite suite, IReadOnlyList<TestTarget>? targets, string reportPath)
    {
        List<string> arguments = [PhpUnitScript(suite.Root, suite.Directory)!, "--log-junit", reportPath];
        if (targets is not null)
        {
            // Test names are "Namespace\Class::method" plus " with data set ..." for data rows.
            var names = targets.Select(t => $"{RegexEscapePhp(ShortClass(t.ClassName))}::{RegexEscapePhp(t.MethodName)}").Distinct(StringComparer.Ordinal);
            arguments.AddRange(["--filter", $"(?:^|\\\\)(?:{string.Join("|", names)})(?: with data set .*)?$"]);
        }
        return new TestCommand(suite, "php", arguments, $"phpunit{Where(suite)}" + Count(targets), reportPath, targets is not null);
    }

    private static string RegexEscapePhp(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ("\\^$.*+?()[]{}|/#".Contains(c)) builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    // --- JVM ---

    private static TestCommand Maven(TestSuite suite, IReadOnlyList<TestTarget>? targets)
    {
        var wrapper = OperatingSystem.IsWindows() ? "mvnw.cmd" : "mvnw";
        var maven = File.Exists(Path.Combine(suite.Directory, wrapper)) ? (OperatingSystem.IsWindows() ? wrapper : "./" + wrapper) : "mvn";
        List<string> arguments = ["-B", "-q", "test"];
        if (targets is not null)
        {
            var selection = targets.Where(t => t.ClassName is not null).GroupBy(t => t.ClassName!, StringComparer.Ordinal)
                .Select(g => $"{g.Key}#{string.Join("+", g.Select(t => t.MethodName).Distinct(StringComparer.Ordinal))}");
            arguments.AddRange([$"-Dtest={string.Join(",", selection)}", "-DfailIfNoTests=false", "-Dsurefire.failIfNoSpecifiedTests=false"]);
        }
        var (program, shellArguments) = Shell(maven, [.. arguments]);
        return new TestCommand(suite, program, shellArguments, $"{maven} test{Where(suite)}" + Count(targets),
            Path.Combine(suite.Directory, "target", "surefire-reports"), targets is not null);
    }

    private static TestCommand Gradle(TestSuite suite, IReadOnlyList<TestTarget>? targets)
    {
        var wrapper = OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew";
        var gradle = File.Exists(Path.Combine(suite.Directory, wrapper)) ? (OperatingSystem.IsWindows() ? wrapper : "./" + wrapper) : "gradle";
        List<string> arguments = ["test", "--console=plain"];
        if (targets is not null)
            foreach (var target in targets.Where(t => t.ClassName is not null)) arguments.AddRange(["--tests", $"{target.ClassName}.{target.MethodName}"]);
        var (program, shellArguments) = Shell(gradle, [.. arguments]);
        return new TestCommand(suite, program, shellArguments, $"{gradle} test{Where(suite)}" + Count(targets),
            Path.Combine(suite.Directory, "build", "test-results", "test"), targets is not null);
    }

    // --- Go, Rust ---

    private static TestCommand GoTest(TestSuite suite, IReadOnlyList<TestTarget>? targets)
    {
        List<string> arguments = ["test", "-json", "-count=1"];
        if (targets is not null) arguments.AddRange(["-run", $"^({string.Join("|", targets.Select(t => Regex.Escape(t.MethodName)).Distinct(StringComparer.Ordinal))})$"]);
        arguments.Add("./...");
        return new TestCommand(suite, "go", arguments, $"go test{Where(suite)}" + Count(targets), null, targets is not null);
    }

    private static TestCommand Cargo(TestSuite suite, IReadOnlyList<TestTarget>? targets)
    {
        List<string> arguments = ["test", "--no-fail-fast"];
        if (targets is not null)
        {
            arguments.Add("--");
            arguments.AddRange(targets.Select(t => t.MethodName).Distinct(StringComparer.Ordinal));
        }
        return new TestCommand(suite, "cargo", arguments, $"cargo test{Where(suite)}" + Count(targets), null, targets is not null);
    }

    // --- helpers ---

    private static bool Accepts(TestSuite suite, string extension) => extension switch
    {
        ".cs" or ".fs" or ".vb" => TestSuite.IsDotNet(suite.Framework),
        ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".mts" or ".cts" => TestSuite.IsNode(suite.Framework),
        ".py" => suite.Framework == TestSuite.Pytest,
        ".php" => suite.Framework == TestSuite.PhpUnit,
        ".java" or ".kt" => suite.Framework == TestSuite.JUnit,
        ".go" => suite.Framework == TestSuite.Go,
        ".rs" => suite.Framework == TestSuite.Rust,
        _ => false
    };

    public static string ShortClass(string? className)
    {
        if (string.IsNullOrEmpty(className)) return string.Empty;
        var cut = className.LastIndexOfAny(['.', '\\', ':']);
        return cut < 0 ? className : className[(cut + 1)..];
    }

    private static string Where(TestSuite suite)
    {
        var folder = Path.GetRelativePath(suite.Root, suite.Directory).Replace('\\', '/');
        return folder == "." ? string.Empty : $" (in {folder})";
    }

    private static string Count(IReadOnlyList<TestTarget>? targets) => targets is null ? string.Empty : $" · {targets.Count} test";

    private static string? Full(string root, string relative)
    {
        try
        {
            var full = Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(root, relative.Replace('\\', '/')));
            return IsInside(full, root) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsInside(string path, string directory)
    {
        var folder = Path.TrimEndingDirectorySeparator(directory);
        return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // A matching file in the folder or a few levels below it, outside dependency and build folders.
    private static bool HasFile(string directory, string pattern)
    {
        try
        {
            var scope = new Workspace.WorkspaceScope(directory, ["vendor", "venv", "target", "build", "dist"]);
            return scope.EnumerateFiles(maxFiles: 20_000).Any(f => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // npm, mvn and gradle are batch files on Windows, which only cmd.exe runs.
    private static (string Program, IReadOnlyList<string> Arguments) Shell(string tool, params string[] arguments) =>
        OperatingSystem.IsWindows() ? ("cmd.exe", ["/d", "/c", tool, .. arguments]) : (tool, arguments);

    private static bool Has(string[] values, string value) => values.Contains(value, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("""^\s*Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SlnProject();

    [GeneratedRegex("""<Project\b[^>]*?\bPath\s*=\s*"(?<path>[^"]+)"\s*""", RegexOptions.IgnoreCase)]
    private static partial Regex SlnxProject();

    [GeneratedRegex(@"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase)]
    private static partial Regex IsTestProject();

    [GeneratedRegex(@"<AssemblyName>([^<]+)</AssemblyName>", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyName();
}

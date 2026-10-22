using System.Text.Json;
using AIThread.Common.Entities;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

// The internal copy of a test's source kept in the catalog, so a machine whose repository lacks the test can get it
// back: the whole test file (as written, line endings included), the test method alone, and the files the test needs
// besides itself (the test project file, conftest.py, phpunit.xml).
public static class UnitTestSnapshot
{
    public const int MaxFileChars = 512 * 1024;
    private const int MaxSupportFileChars = 200 * 1024;
    private const int MaxSupportChars = 600 * 1024;

    private sealed record SupportFile(string Path, string Content);

    // Fills the snapshot from the files on disk; false (snapshot left as it was) when the test file cannot be read.
    public static bool Capture(string root, UnitTestCase test, TestSuite? suite, string? commit, string? branch)
    {
        var content = Read(root, test.TestFile, MaxFileChars);
        if (content is null) return false;

        test.SourceSnapshot = content;
        test.SourceHash = UnitTestText.Hash(content);
        test.SourceCommit = commit;
        test.SourceBranch = branch is { Length: > 200 } ? branch[..200] : branch;
        var discovered = suite is null ? null : TestDiscovery.InContent(test.TestFile, content, suite).FirstOrDefault(d => d.Selector == test.Selector);
        test.MethodSnapshot = discovered is null ? null : TestDiscovery.Slice(content, discovered.StartLine, discovered.EndLine);
        test.SupportFiles = suite is null ? null : Support(root, test, suite);
        return true;
    }

    public static void Clear(UnitTestCase test)
    {
        test.SourceSnapshot = null;
        test.SourceHash = null;
        test.MethodSnapshot = null;
        test.SupportFiles = null;
    }

    // The support files of a snapshot as (relative path, content).
    public static IReadOnlyList<(string Path, string Content)> SupportFiles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<SupportFile>>(json, JsonSerializerOptions.Web) ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f.Path) && f.Content is not null)
                .Select(f => (f.Path, f.Content))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Support(string root, UnitTestCase test, TestSuite suite)
    {
        var paths = new List<string>();
        var suiteFolder = Path.GetRelativePath(root, suite.Directory).Replace('\\', '/');
        string Join(string file) => suiteFolder == "." ? file : $"{suiteFolder}/{file}";
        if (TestSuite.IsDotNet(suite.Framework))
        {
            paths.Add(suite.Manifest);
            paths.AddRange(new[] { "GlobalUsings.cs", "Usings.cs" }.Select(Join));
        }
        else if (suite.Framework == TestSuite.Pytest)
        {
            paths.AddRange(new[] { "conftest.py", "pytest.ini" }.Select(Join));
            // conftest.py of every folder between the suite and the test.
            var folder = Path.GetDirectoryName(test.TestFile.Replace('\\', '/'))?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && !string.Equals(folder, suiteFolder, StringComparison.OrdinalIgnoreCase))
            {
                paths.Add($"{folder}/conftest.py");
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }
        else if (suite.Framework == TestSuite.PhpUnit)
        {
            paths.AddRange(new[] { "phpunit.xml", "phpunit.xml.dist", "phpunit.dist.xml" }.Select(Join));
        }

        var files = new List<SupportFile>();
        var total = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(path, test.TestFile, StringComparison.OrdinalIgnoreCase)) continue;
            var content = Read(root, path, MaxSupportFileChars);
            if (content is null || total + content.Length > MaxSupportChars) continue;
            total += content.Length;
            files.Add(new SupportFile(path, content));
        }
        return files.Count == 0 ? null : JsonSerializer.Serialize(files, JsonSerializerOptions.Web);
    }

    private static string? Read(string root, string relative, int maxChars)
    {
        try
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(full);
            if (!info.Exists || info.Length > maxChars * 4L) return null;
            var content = File.ReadAllText(full);
            return content.Length <= maxChars ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

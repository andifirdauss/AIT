using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.UnitTests;

// How a test found in the code becomes a catalog entry, and where its code under test probably is.
public static class UnitTestEntries
{
    private const int MaxGuessedTargets = 3;

    private static readonly string[] TestSuffixes = ["Tests", "Test", "Specs", "Spec", "_test", "_spec", ".test", ".spec"];

    public static UnitTestCase FromDiscovered(DiscoveredTest test, string projectKey, string? projectName, UnitTestOrigin origin, string? by, Guid? threadId) => new()
    {
        ProjectKey = projectKey,
        ProjectName = projectName,
        Title = UnitTestText.Clip(UnitTestText.Humanize(test.MethodName), 300),
        Framework = test.Framework,
        TestProject = test.TestProject,
        TestFile = test.File,
        ClassName = test.ClassName,
        MethodName = test.MethodName,
        Selector = UnitTestText.Clip(test.Selector, UnitTestCase.MaxSelectorLength),
        Origin = origin,
        CreatedBy = by,
        CreatedThreadId = threadId
    };

    // Files named like the test without its test suffix ("UserServiceTests.cs" → UserService.*, "test_calc.py" →
    // calc.py, "user.spec.ts" → user.ts), outside test folders: the usual home of the code under test.
    public static IReadOnlyList<string> GuessTargets(string testFile, string? className, IEnumerable<string> projectFiles)
    {
        var stems = new[] { Stem(Path.GetFileNameWithoutExtension(testFile)), Stem(Unqualified(className)) }
            .Where(s => s.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (stems.Count == 0) return [];
        var extension = Path.GetExtension(testFile);
        return projectFiles
            .Where(f => !string.Equals(f, testFile, StringComparison.OrdinalIgnoreCase) && !IsTestPath(f)
                        && stems.Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Length)
            .Take(MaxGuessedTargets)
            .ToList();
    }

    private static string Stem(string name)
    {
        var stem = name;
        if (stem.StartsWith("test_", StringComparison.OrdinalIgnoreCase)) stem = stem[5..];
        foreach (var suffix in TestSuffixes)
            if (stem.EndsWith(suffix, StringComparison.Ordinal) && stem.Length > suffix.Length)
                return stem[..^suffix.Length];
        return stem;
    }

    private static string Unqualified(string? className) => string.IsNullOrEmpty(className)
        ? string.Empty
        : className[(className.LastIndexOfAny(['.', '\\', '+', '/']) + 1)..];

    private static bool IsTestPath(string path)
    {
        var normalized = "/" + path.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        return normalized.Contains("/test/") || normalized.Contains("/tests/") || normalized.Contains("/__tests__/") || normalized.Contains(".tests/")
               || TestSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)) || name.StartsWith("test_", StringComparison.OrdinalIgnoreCase);
    }
}

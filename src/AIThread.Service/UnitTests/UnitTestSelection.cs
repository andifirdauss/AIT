using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.UnitTests;

// Which catalog tests a change concerns: the tests in a file it touches, the tests whose target files it touches, and
// the tests of the modules it touches. Mode All takes every active test.
public static class UnitTestSelection
{
    public static IReadOnlyList<UnitTestCase> Relevant(
        IEnumerable<UnitTestCase> tests,
        UnitTestMode mode,
        IReadOnlyCollection<string> files,
        IReadOnlyCollection<string> modules)
    {
        var runnable = tests.Where(t => t.State == UnitTestState.Active).ToList();
        if (mode == UnitTestMode.All) return runnable;
        if (mode == UnitTestMode.Off) return [];

        var fileSet = files.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moduleSet = modules.Where(m => !string.IsNullOrWhiteSpace(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return runnable.Where(t =>
                fileSet.Contains(Normalize(t.TestFile))
                || UnitTestText.Lines(t.TargetFiles).Any(f => fileSet.Contains(Normalize(f)))
                || (t.Module is not null && moduleSet.Contains(t.Module)))
            .ToList();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim().TrimStart('.', '/');
}

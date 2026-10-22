using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestExecutor
{
    // The runnable suite of every test (the one its catalog entry names, else the one its file lies in). Tests without
    // one get their Skipped verdict here.
    private static List<(TestSuite Suite, List<UnitTestCase> Tests)> Group(string root, IReadOnlyList<TestSuite> suites, IReadOnlyList<UnitTestCase> tests,
        Dictionary<Guid, TestVerdict> verdicts)
    {
        var groups = new Dictionary<TestSuite, List<UnitTestCase>>();
        foreach (var test in tests)
        {
            var suite = suites.FirstOrDefault(s => test.TestProject is not null && string.Equals(s.Manifest, test.TestProject, StringComparison.OrdinalIgnoreCase)
                                                   && TestCommandResolver.SuiteFor([s], root, test.TestFile) is not null)
                        ?? TestCommandResolver.SuiteFor(suites, root, test.TestFile);
            if (suite is null)
            {
                verdicts[test.Id] = new TestVerdict(UnitTestResult.Skipped, null, null, "Tidak ada runner test untuk file ini di project (suite tidak ditemukan).");
                continue;
            }
            if (!suite.CanRun)
            {
                verdicts[test.Id] = new TestVerdict(UnitTestResult.Skipped, null, null, suite.Problem);
                continue;
            }
            if (!groups.TryGetValue(suite, out var group)) groups[suite] = group = [];
            group.Add(test);
        }
        return groups.Select(g => (g.Key, g.Value)).ToList();
    }
}

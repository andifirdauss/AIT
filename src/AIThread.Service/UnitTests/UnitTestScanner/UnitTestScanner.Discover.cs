using AIThread.Service.Models;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    private static IReadOnlyList<DiscoveredTest> Discover(ProjectContext project, IReadOnlyList<TestSuite> suites, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (suites.Count == 0) return [];
        var candidates = TestDiscovery.Candidates(suites, project.RootPath, files);
        var tests = new List<DiscoveredTest>();
        foreach (var (file, suite) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tests.AddRange(TestDiscovery.InFile(project.RootPath, file, suite));
        }
        return tests;
    }
}

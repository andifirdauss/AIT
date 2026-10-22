using AIThread.Service.Orchestration;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // Another test file of the same suite, as the style an executor follows.
    private static ContextRef? Example(IReadOnlyList<DiscoveredTest> discovered, TestSuite? suite, string file)
    {
        if (suite is null) return null;
        var sibling = discovered.Where(d => d.TestProject == suite.Manifest && !string.Equals(d.File, file, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.File)
            .OrderBy(f => string.Equals(Path.GetDirectoryName(f), Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        return sibling is null ? null : new ContextRef { File = sibling, StartLine = 1, EndLine = ExampleLines, Note = "an existing test file of the same suite: follow its style" };
    }
}

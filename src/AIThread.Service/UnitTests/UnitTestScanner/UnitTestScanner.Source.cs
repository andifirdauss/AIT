using AIThread.Common.Entities;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    // The test's own lines as they are now (else its snapshot), cut for the label prompt.
    private static string? Source(string root, UnitTestCase test, IReadOnlyList<TestSuite> suites)
    {
        var suite = TestCommandResolver.SuiteFor(suites, root, test.TestFile);
        var found = suite is null ? null : TestDiscovery.InFile(root, test.TestFile, suite).FirstOrDefault(d => d.Selector == test.Selector);
        string? source = null;
        if (found is not null)
        {
            try
            {
                source = TestDiscovery.Slice(File.ReadAllText(Path.Combine(root, test.TestFile)), found.StartLine, found.EndLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                source = null;
            }
        }
        source ??= test.MethodSnapshot;
        return source is null ? null : UnitTestText.Clip(source, MaxSourceCharsForLabel);
    }
}

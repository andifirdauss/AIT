using AIThread.Common.Entities;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The test's code as it is now, for the tester.
    private static string? Source(UnitTestSession session, UnitTestCase test)
    {
        var root = session.Project.RootPath;
        var suite = TestCommandResolver.SuiteFor(session.Suites, root, test.TestFile);
        var found = suite is null ? null : TestDiscovery.InFile(root, test.TestFile, suite).FirstOrDefault(d => d.Selector == test.Selector);
        if (found is null) return test.MethodSnapshot is null ? null : UnitTestText.Clip(test.MethodSnapshot, MaxSourceChars);
        try
        {
            return UnitTestText.Clip(TestDiscovery.Slice(File.ReadAllText(Path.Combine(root, test.TestFile)), found.StartLine, found.EndLine), MaxSourceChars);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

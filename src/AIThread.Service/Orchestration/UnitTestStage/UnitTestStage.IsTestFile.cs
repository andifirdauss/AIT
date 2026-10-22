namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // A file holding catalog tests or tests of this change.
    private static bool IsTestFile(UnitTestSession session, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('.', '/');
        return session.Tests.Concat(session.NewTests).Any(t => string.Equals(t.TestFile, normalized, StringComparison.OrdinalIgnoreCase));
    }
}

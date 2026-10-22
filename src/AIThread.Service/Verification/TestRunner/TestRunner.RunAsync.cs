namespace AIThread.Service.Verification;

public sealed partial class TestRunner
{
    public async Task<TestRunResult> RunAsync(TestSuite suite, IReadOnlyList<TestTarget>? targets, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var reportDirectory = Path.Combine(Path.GetTempPath(), "aithread-test-reports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportDirectory);
        var reportFile = Path.Combine(reportDirectory, suite.Format == TestReportFormat.JUnitXml ? "report.xml" : "report.json");
        var command = TestCommandResolver.Command(suite, targets, reportFile);

        var gate = RootGates.For(suite.Root);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunCoreAsync(command, timeout, cancellationToken);
        }
        finally
        {
            gate.Release();
            try
            {
                Directory.Delete(reportDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A runner process that outlived the kill may still hold a file; the OS temp folder is cleaned eventually.
            }
        }
    }
}

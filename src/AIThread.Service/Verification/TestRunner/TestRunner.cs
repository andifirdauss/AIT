using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

// Runs a TestCommand like BuildRunner runs a build: one run per project root at a time (RootGates), the process tree
// stopped on timeout or cancellation, output streamed. The report goes to a folder of AIT's own that is removed
// afterwards; go and cargo report on their output, which is kept in full (up to MaxOutputChars) for the parser.
public sealed partial class TestRunner(ILogger<TestRunner> logger) : ITestRunner
{
    private const int TailLines = 80;
    private const int MaxOutputChars = 16 * 1024 * 1024;

    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);
}

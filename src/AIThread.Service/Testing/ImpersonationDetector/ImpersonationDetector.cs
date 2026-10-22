using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

// "Login as" of a project: ImpersonationScanner finds the code places, the cheap MemoryAi node reads them, and the
// result goes to the project's testing settings. Without hits no model is asked; without a cheap node the hits stay
// "unknown" with their places as evidence, for the developer to fill in.
public sealed partial class ImpersonationDetector(IMemoryAi ai, ITestAccountStore store, ILogger<ImpersonationDetector> logger) : IImpersonationDetector
{
    private const int MaxAnswerTokens = 1_500;
    private const int MaxEvidence = 8;
}

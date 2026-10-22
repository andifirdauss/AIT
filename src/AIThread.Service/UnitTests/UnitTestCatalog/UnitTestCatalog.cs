using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

// Reads and writes the unit test catalog next to long memory (LongMemoryStore.CreateUnitTestsAsync), so a shared
// external store shares the catalog with every developer of the project.
public sealed partial class UnitTestCatalog(LongMemoryStore store, ILogger<UnitTestCatalog> logger) : IUnitTestCatalog
{
    // History rows kept per test.
    public const int MaxRunsPerTest = 20;

    public const int MinTimeoutSeconds = 10;
    public const int MaxTimeoutSeconds = 3_600;

    // Two developers adding tests at the same moment can pick the same number; the loser tries the next one.
    private const int MaxNumberAttempts = 5;
}

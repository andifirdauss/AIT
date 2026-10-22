using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

// Builds the catalog from the tests already in a repository (Project > Detail → Unit test → Scan test, and the first
// thread of a project with tests): static discovery, the code map for the module, names for the targets, and a cheap
// model (MemoryAi) for a readable title and purpose. The project is walked fresh every time: a test file moved a moment
// ago must be seen where it is now, or the restorer would put it back where it was.
public sealed partial class UnitTestScanner(
    IUnitTestCatalog catalog,
    ICodeMapper codeMapper,
    IMemoryAi ai,
    ILogger<UnitTestScanner> logger) : IUnitTestScanner
{
    private const int LabelBatchSize = 12;
    private const int MaxLabelBatches = 10;
    private const int MaxSourceCharsForLabel = 1_200;
    private const int MaxFiles = 100_000;
}

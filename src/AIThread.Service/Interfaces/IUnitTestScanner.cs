using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Interfaces;

public interface IUnitTestScanner
{
    // The tests in the project's files, found without running anything (Struktur kode's walk of the project).
    Task<IReadOnlyList<DiscoveredTest>> DiscoverAsync(ProjectContext project, IReadOnlyList<TestSuite> suites, CancellationToken cancellationToken);

    // Discovery merged into the catalog: new tests are added (Scanned), moved ones follow their file. label: a cheap
    // model also writes title, purpose, scenario and targets of tests that have none yet.
    Task<UnitTestScanResultDto> ScanAsync(ProjectContext project, bool label, string by, CancellationToken cancellationToken);
}

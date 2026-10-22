using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Interfaces;

public interface IUnitTestRestorer
{
    // Puts back the active catalog tests the repository lacks: a test that moved follows its new file, a missing file
    // comes back from its snapshot, a missing method goes back into its file. What needs a model (a method of another
    // language, a test without a snapshot written again from its scenario) comes back as executor tasks. Never part of a
    // thread's rollback.
    Task<UnitTestRestoreResult> RestoreAsync(ProjectContext project, IReadOnlyList<UnitTestCase> tests, IReadOnlyList<TestSuite> suites,
        CancellationToken cancellationToken);
}

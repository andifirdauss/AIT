using AIThread.Service.Models;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    public Task<IReadOnlyList<DiscoveredTest>> DiscoverAsync(ProjectContext project, IReadOnlyList<TestSuite> suites, CancellationToken cancellationToken) =>
        Task.Run(() => Discover(project, suites, Files(project, cancellationToken), cancellationToken), cancellationToken);
}

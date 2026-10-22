using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestCatalogDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var settings = await catalog.GetSettingsAsync(project.Key, cancellationToken);
        var suites = TestCommandResolver.Suites(project.RootPath, project.Modules)
            .Select(s => new UnitTestSuiteDto(s.Framework, s.Stack, s.Manifest, s.PerTestResults, s.Problem))
            .ToList();
        var tests = await catalog.ListAsync(project.Key, cancellationToken);
        var tester = (await nodeService.GetActiveAsync(NodeRole.Tester, cancellationToken)).Count > 0;
        return new UnitTestCatalogDto(project.Key, UnitTestCatalog.ToDto(settings), suites, tests.Select(UnitTestCatalog.ToDto).ToList(), tester);
    }
}

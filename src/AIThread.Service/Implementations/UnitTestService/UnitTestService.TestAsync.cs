using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    private async Task<(ProjectContext Project, UnitTestCase Test)> TestAsync(Guid projectId, Guid testId, CancellationToken cancellationToken)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var test = await catalog.GetAsync(testId, cancellationToken);
        if (test is null || test.ProjectKey != project.Key) throw new AppValidationException(Lang.T("Unit test tidak ditemukan di project ini.", "Unit test not found in this project."));
        return (project, test);
    }
}

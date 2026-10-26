using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestCaseDto> ReactivateAsync(Guid projectId, Guid testId, CancellationToken cancellationToken = default)
    {
        var (project, test) = await TestAsync(projectId, testId, cancellationToken);
        if (test.State is not (UnitTestState.Obsolete or UnitTestState.Quarantined))
            throw new AppValidationException(Lang.T($"{test.DisplayNumber} tidak sedang obsolete atau dikarantina.", $"{test.DisplayNumber} is not obsolete or quarantined."));

        var was = test.State;
        test.State = UnitTestState.Active;
        test.ObsoleteReason = null;
        test.ObsoleteThreadId = null;
        test.ObsoleteBy = null;
        test.ObsoleteAt = null;
        test.QuarantineReason = null;
        await catalog.UpdateAsync([test], cancellationToken);
        activityLog.Info("unittest", Lang.T($"{test.DisplayNumber} {test.Title} diaktifkan lagi ({(was == UnitTestState.Obsolete ? "batal obsolete" : "keluar dari karantina")}).", $"{test.DisplayNumber} {test.Title} activated again ({(was == UnitTestState.Obsolete ? "no longer obsolete" : "out of quarantine")})."),
            projectId: project.ProjectId);
        return UnitTestCatalog.ToDto(test);
    }
}

using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestCaseDto> ReactivateAsync(Guid projectId, Guid testId, CancellationToken cancellationToken = default)
    {
        var (project, test) = await TestAsync(projectId, testId, cancellationToken);
        if (test.State is not (UnitTestState.Obsolete or UnitTestState.Quarantined))
            throw new AppValidationException($"{test.DisplayNumber} tidak sedang obsolete atau dikarantina.");

        var was = test.State;
        test.State = UnitTestState.Active;
        test.ObsoleteReason = null;
        test.ObsoleteThreadId = null;
        test.ObsoleteBy = null;
        test.ObsoleteAt = null;
        test.QuarantineReason = null;
        await catalog.UpdateAsync([test], cancellationToken);
        activityLog.Info("unittest", $"{test.DisplayNumber} {test.Title} diaktifkan lagi ({(was == UnitTestState.Obsolete ? "batal obsolete" : "keluar dari karantina")}).",
            projectId: project.ProjectId);
        return UnitTestCatalog.ToDto(test);
    }
}

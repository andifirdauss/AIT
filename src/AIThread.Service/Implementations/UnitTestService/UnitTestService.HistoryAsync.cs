using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<IReadOnlyList<UnitTestHistoryDto>> HistoryAsync(Guid projectId, Guid testId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var test = await catalog.GetAsync(testId, cancellationToken);
        if (test is null || test.ProjectKey != project.Key) throw new AppValidationException("Unit test tidak ditemukan di project ini.");
        return (await catalog.HistoryAsync(testId, HistoryRows, cancellationToken))
            .Select(r => new UnitTestHistoryDto(r.Id, r.Phase, r.Result, r.DurationMs, r.Message, r.SkipReason, r.Machine, r.RunBy, r.ThreadId, r.CreatedAt))
            .ToList();
    }
}

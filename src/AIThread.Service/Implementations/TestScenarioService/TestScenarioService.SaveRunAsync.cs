using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task SaveRunAsync(Guid scenarioId, TestingReport report, string by, CancellationToken cancellationToken = default)
    {
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        if (await db.TestingScenarios.FirstOrDefaultAsync(s => s.Id == scenarioId, cancellationToken) is not { } scenario) return;
        scenario.LastRun = report.ToJson();
        scenario.LastRunAt = DateTime.UtcNow;
        scenario.LastRunBy = by;
        await db.SaveChangesAsync(cancellationToken);
    }
}

using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<TestingScenario?> GetAsync(Guid scenarioId, CancellationToken cancellationToken = default)
    {
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        return await db.TestingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, cancellationToken);
    }
}

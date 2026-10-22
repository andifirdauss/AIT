using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<ScenarioFile> ExcelAsync(Guid projectId, Guid scenarioId, CancellationToken cancellationToken = default)
    {
        var (project, _) = await ProjectAsync(projectId, cancellationToken);
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenario = await db.TestingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId && s.ProjectKey == key, cancellationToken)
                       ?? throw new AppValidationException("Skenario tidak ditemukan.");
        return new ScenarioFile(ScenarioSheet.Write(scenario, await accounts.GetAsync(key, cancellationToken)), $"{scenario.Slug}.xlsx");
    }
}

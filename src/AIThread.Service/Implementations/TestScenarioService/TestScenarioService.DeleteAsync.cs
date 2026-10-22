using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task DeleteAsync(Guid projectId, Guid scenarioId, CancellationToken cancellationToken = default)
    {
        var (project, _) = await ProjectAsync(projectId, cancellationToken);
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenario = await db.TestingScenarios.FirstOrDefaultAsync(s => s.Id == scenarioId && s.ProjectKey == key, cancellationToken)
                       ?? throw new AppValidationException("Skenario tidak ditemukan.");
        db.TestingScenarios.Remove(scenario);
        await db.SaveChangesAsync(cancellationToken);
        activityLog.Info(LogCategory, $"Skenario test \"{scenario.Title}\" dihapus.", projectId: projectId);
    }
}

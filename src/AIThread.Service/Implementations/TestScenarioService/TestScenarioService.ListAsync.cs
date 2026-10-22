using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<IReadOnlyList<TestingScenarioDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        if (string.IsNullOrEmpty(project.Key)) return [];
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenarios = await db.TestingScenarios.AsNoTracking().Where(s => s.ProjectKey == key).ToListAsync(cancellationToken);
        return scenarios.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).Select(ScenarioText.ToDto).ToList();
    }
}

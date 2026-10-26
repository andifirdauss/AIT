using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<string> WriteToProjectAsync(ProjectContext project, Guid scenarioId, CancellationToken cancellationToken = default)
    {
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var key = project.Key;
        var scenario = await db.TestingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId && s.ProjectKey == key, cancellationToken)
                       ?? throw new AppValidationException(Lang.T("Skenario tidak ditemukan.", "Scenario not found."));
        var relative = $"{SheetFolder}/{scenario.Slug}.xlsx";
        var full = Path.Combine(project.RootPath, SheetFolder.Replace('/', Path.DirectorySeparatorChar), scenario.Slug + ".xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, ScenarioSheet.Write(scenario, await accounts.GetAsync(key, cancellationToken)), cancellationToken);
        return relative;
    }
}

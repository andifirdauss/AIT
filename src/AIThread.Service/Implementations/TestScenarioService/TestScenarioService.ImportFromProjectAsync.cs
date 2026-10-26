using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<ScenarioImportResultDto?> ImportFromProjectAsync(ProjectContext project, TestingScenario scenario, CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(project.RootPath, SheetFolder.Replace('/', Path.DirectorySeparatorChar), scenario.Slug + ".xlsx");
        if (!File.Exists(full)) return null;
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        try
        {
            await using var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await ImportSheetAsync(project, file, scenario.Id, by, cancellationToken);
        }
        catch (IOException ex)
        {
            throw new AppValidationException(Lang.T($"File {SheetFolder}/{scenario.Slug}.xlsx tidak bisa dibaca ({ex.Message}); tutup dulu di Excel bila sedang terbuka.", $"The file {SheetFolder}/{scenario.Slug}.xlsx cannot be read ({ex.Message}); close it in Excel first if it is open."));
        }
    }
}

using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestSettingsDto> SaveSettingsAsync(Guid projectId, SaveUnitTestSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        var settings = await catalog.SaveSettingsAsync(project.Key, request, by, cancellationToken);
        activityLog.Info("unittest", $"Setting unit test {project.Name}: mode {settings.Mode}, test baru {settings.Authoring}, snapshot {(settings.KeepSnapshots ? "disimpan" : "tidak disimpan")}.",
            projectId: projectId);
        return UnitTestCatalog.ToDto(settings);
    }
}

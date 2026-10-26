using AIThread.Common.DTOs;
using AIThread.Common.Localization;
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
        activityLog.Info("unittest", Lang.T($"Pengaturan unit test {project.Name}: mode {settings.Mode}, test baru {settings.Authoring}, snapshot {(settings.KeepSnapshots ? "disimpan" : "tidak disimpan")}.", $"Unit test settings of {project.Name}: mode {settings.Mode}, new tests {settings.Authoring}, snapshots {(settings.KeepSnapshots ? "kept" : "not kept")}."),
            projectId: projectId);
        return UnitTestCatalog.ToDto(settings);
    }
}

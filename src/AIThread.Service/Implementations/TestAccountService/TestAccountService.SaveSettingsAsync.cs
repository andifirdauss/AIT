using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestingSettingsDto> SaveSettingsAsync(Guid projectId, SaveTestingSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ContextAsync(projectId, cancellationToken);
        var settings = await store.SaveDevPasswordAsync(project.Key, request.DevPassword, TestingSettings.SourceDeveloper, by, cancellationToken);
        activityLog.Info(LogCategory, string.IsNullOrEmpty(settings.DevPassword)
            ? Lang.T($"Password dev {project.Name} kembali ke bawaan.", $"The dev password of {project.Name} is back to the default.")
            : Lang.T($"Password dev {project.Name} diubah.", $"The dev password of {project.Name} changed."), projectId: projectId);
        return TestAccountText.ToDto(settings);
    }
}

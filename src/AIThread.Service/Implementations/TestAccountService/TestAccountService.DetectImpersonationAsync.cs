using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestingSettingsDto> DetectImpersonationAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ContextAsync(projectId, cancellationToken);
        var info = await impersonation.DetectAsync(project, by, cancellationToken);
        activityLog.Info(LogCategory, Lang.T($"Deteksi login as {project.Name}: {info?.Status ?? ImpersonationInfo.Unknown}.", $"Login as detection for {project.Name}: {info?.Status ?? ImpersonationInfo.Unknown}."), projectId: projectId);
        return TestAccountText.ToDto((await store.GetAsync(project.Key, cancellationToken)).Settings);
    }
}

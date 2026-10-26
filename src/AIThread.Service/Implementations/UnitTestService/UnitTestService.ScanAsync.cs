using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestScanResultDto> ScanAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        var result = await scanner.ScanAsync(project, label: true, by, cancellationToken);
        activityLog.Info("unittest", Lang.T($"Scan unit test {project.Name}: {result.Found} test ditemukan, {result.Added} baru, {result.Moved} pindah file, {result.Labeled} diberi label.", $"Unit test scan of {project.Name}: {result.Found} tests found, {result.Added} new, {result.Moved} moved file, {result.Labeled} labeled."),
            projectId: projectId);
        return result;
    }
}

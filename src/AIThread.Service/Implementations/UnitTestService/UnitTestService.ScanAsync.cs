using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestScanResultDto> ScanAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        var result = await scanner.ScanAsync(project, label: true, by, cancellationToken);
        activityLog.Info("unittest", $"Scan unit test {project.Name}: {result.Found} test ditemukan, {result.Added} baru, {result.Moved} pindah file, {result.Labeled} diberi label.",
            projectId: projectId);
        return result;
    }
}

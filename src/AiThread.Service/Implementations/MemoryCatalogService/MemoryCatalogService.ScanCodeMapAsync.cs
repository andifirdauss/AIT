using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<CodeMapScanResultDto> ScanCodeMapAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var (files, mapped) = await codeMapper.ScanAsync(project, ScanMaxFiles, cancellationToken);
        var modules = (await registry.ListAsync(project.Key, cancellationToken: cancellationToken)).Count;
        return new CodeMapScanResultDto(files, mapped, modules);
    }
}

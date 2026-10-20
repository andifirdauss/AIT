using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<IReadOnlyList<CodeMapEntryDto>> GetCodeMapAsync(Guid projectId, string? search, string? module, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, false, cancellationToken);
        var rows = await codeMapper.ListAsync(key, search, module, 500, cancellationToken);
        return rows.Select(ToDto).ToList();
    }
}

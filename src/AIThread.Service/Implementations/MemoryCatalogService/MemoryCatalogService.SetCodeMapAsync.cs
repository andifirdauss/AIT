using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<CodeMapEntryDto> SetCodeMapAsync(Guid projectId, SetCodeMapRequest request, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var target = request.Path;
        if (!target.Contains('/') && !target.Contains('\\') && !Path.HasExtension(target))
        {
            var found = await codeMapper.ResolveReferencesAsync(project, target, cancellationToken);
            target = found.Count switch
            {
                0 => throw new AppValidationException($"Tidak ada file bernama '{request.Path}' di project ini."),
                1 => found[0],
                _ => throw new AppValidationException($"'{request.Path}' cocok dengan beberapa file ({string.Join(", ", found.Take(5))}). Isi path lengkapnya.")
            };
        }
        if (string.IsNullOrWhiteSpace(request.Module)) throw new AppValidationException("Modul wajib diisi.");
        var hit = await codeMapper.SetManualAsync(project, target, request.Module, request.Layer, cancellationToken);
        var stored = await codeMapper.StoredAsync(project.Key, [hit.Path], cancellationToken);
        return ToDto(stored[hit.Path]);
    }
}

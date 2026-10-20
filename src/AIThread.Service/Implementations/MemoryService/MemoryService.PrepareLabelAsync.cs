using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Validates and normalizes a label: the module resolved against (or added to) the registry of the memory's key.
    private async Task<PreparedLabel> PrepareLabelAsync(SaveMemoryRequest request, ProjectScope? owner, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Type) || request.Type == MemoryType.Business)
            throw new AppValidationException("Tipe memory tidak valid (business memory dikelola di tab Bisnis).");
        if (request.Type == MemoryType.Project && request.ProjectId is null)
            throw new AppValidationException("Project memory harus terkait dengan sebuah project.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException("Deskripsi wajib diisi.");
        if (MemoryLabels.Module(request.Module) is not { } module) throw new AppValidationException("Modul wajib diisi.");
        if (!Enum.IsDefined(request.Kind)) throw new AppValidationException("Jenis memory wajib dipilih.");
        if (!MemoryLabels.KindAllowedFor(request.Type, request.Kind))
            throw new AppValidationException("Knowledge memory hanya boleh berjenis Pitfall, Pattern, atau Architecture.");
        var layers = (request.Layers ?? []).Where(Enum.IsDefined).Distinct().Order().ToList();

        var extra = MemoryLabels.ExtraModules(request.ExtraModules, module);

        var key = request.Type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey : owner?.Key;
        if (key is not null)
        {
            try
            {
                (module, _) = await registry.EnsureAsync(key, module, cancellationToken: cancellationToken);
                var resolved = new List<string>(extra.Count);
                foreach (var name in extra) resolved.Add((await registry.EnsureAsync(key, name, cancellationToken: cancellationToken)).Name);
                // Two names may resolve to the same module (an alias of the main one, say).
                extra = MemoryLabels.ExtraModules(resolved, module);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not AppValidationException)
            {
                // The registry lives with long memory; while that store is down the names are kept as written.
                LongStoreFailed(ex, "Could not resolve the module of a memory");
            }
        }

        var keywords = MemoryLabels.Keywords(request.Keywords, [module, .. extra]);
        if (keywords.Count == 0) throw new AppValidationException("Isi minimal satu keyword.");
        var area = MemoryLabels.Area(request.Area);
        return new PreparedLabel(module, extra, layers, request.Kind, area, keywords,
            MemoryLabels.Compose(MemoryLabels.Display(area, module, layers, request.Kind, extra), keywords));
    }
}

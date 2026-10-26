using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Validates and normalizes a label: the module resolved against (or added to) the registry of the memory's key.
    private async Task<PreparedLabel> PrepareLabelAsync(SaveMemoryRequest request, ProjectScope? owner, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Type) || request.Type == MemoryType.Business)
            throw new AppValidationException(Lang.T("Tipe memory tidak valid (business memory dikelola di tab Business memory).", "Invalid memory type (business memory is managed in the Business memory tab)."));
        if (request.Type == MemoryType.Project && request.ProjectId is null)
            throw new AppValidationException(Lang.T("Project memory harus terkait dengan sebuah project.", "A project memory must belong to a project."));
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException(Lang.T("Deskripsi wajib diisi.", "The description is required."));
        if (MemoryLabels.Module(request.Module) is not { } module) throw new AppValidationException(Lang.T("Modul wajib diisi.", "The module is required."));
        if (!Enum.IsDefined(request.Kind)) throw new AppValidationException(Lang.T("Jenis memory wajib dipilih.", "The memory kind must be chosen."));
        if (!MemoryLabels.KindAllowedFor(request.Type, request.Kind))
            throw new AppValidationException(Lang.T("Knowledge memory hanya boleh berjenis Pitfall, Pattern, atau Architecture.", "Knowledge memory can only be of kind Pitfall, Pattern or Architecture."));
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
        if (keywords.Count == 0) throw new AppValidationException(Lang.T("Isi minimal satu keyword.", "Fill in at least one keyword."));
        var area = MemoryLabels.Area(request.Area);
        return new PreparedLabel(module, extra, layers, request.Kind, area, keywords,
            MemoryLabels.Compose(MemoryLabels.Display(area, module, layers, request.Kind, extra), keywords));
    }
}

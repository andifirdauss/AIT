using System.Reflection;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // Resolves the memory's project on this machine: the same project id (a restore on the machine that exported it)
    // when its key agrees, else a clone registered under the same key.
    private static Prepared Prepare(MemoryExportItem item, LocalProjects projects)
    {
        var knowledge = item.Type == MemoryType.Knowledge;
        var projectKey = string.IsNullOrWhiteSpace(item.ProjectKey) ? null : item.ProjectKey.Trim();
        var local = knowledge ? null
            : projects.Find(item.ProjectId) is { } byId && (projectKey is null || byId.Key is null || byId.Key == projectKey) ? byId
            : projectKey is null ? null : projects.FindByKey(projectKey);
        var key = knowledge ? null : projectKey ?? local?.Key;
        var orphaned = !knowledge && key is null && local is null;
        var type = orphaned ? MemoryType.Knowledge : item.Type;
        var name = type == MemoryType.Knowledge ? null : Trim(item.ProjectName, 200) ?? local?.Name;

        // A label is kept only when it is complete and valid for the (possibly changed) type.
        ImportedLabel? labels = null;
        if (MemoryLabels.Module(item.Module) is { } module && item.Kind is { } kind && Enum.IsDefined(kind) && MemoryLabels.KindAllowedFor(type, kind))
        {
            var extra = MemoryLabels.ExtraModules(item.ExtraModules, module);
            var keywords = MemoryLabels.Keywords(item.Keywords, [module, .. extra]);
            if (keywords.Count > 0)
                labels = new ImportedLabel(module, (item.Layers ?? []).Where(Enum.IsDefined).Distinct().Order().ToList(), kind, MemoryLabels.Area(item.Area), keywords, extra);
        }
        var label = labels is null ? (item.Label ?? string.Empty).Trim() : labels.Composed;

        return new Prepared(item.Id, item.Level, type, local?.Id, key, name, label, item.Description.Trim(),
            item.SourceThreadId, Utc(item.CreatedAt), item.PromotedAt is { } promoted ? Utc(promoted) : null, Math.Max(0, item.HitCount),
            item.LastAccessedAt is { } accessed ? Utc(accessed) : null, orphaned, labels, Trim(item.LegacyLabel, 300));
    }
}

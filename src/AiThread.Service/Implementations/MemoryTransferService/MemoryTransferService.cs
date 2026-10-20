using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Memory travels by ProjectKey, so a file exported on one machine lands on the same project on another, whatever its
// root folder. The rules follow the copy made when switching stores: ids are kept (re-importing never duplicates),
// a project that is not registered here is dropped from the row but its key stays, and project memory without any
// key becomes knowledge. A labeled item keeps its label (its module joins the key's module list); an unlabeled one
// (format v1, or written by an older version) is relabeled later by MemoryRelabelWorker.
public sealed partial class MemoryTransferService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    IModuleRegistry registry,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<MemoryTransferService> logger) : IMemoryTransferService
{
    private const int MaxItems = 100_000;
    private const int BatchSize = 200;
    private const int MaxListedWarnings = 5;

    private sealed record ExistingLong(Guid Id, string? ProjectKey, Guid? ProjectId, MemoryType Type, string Label, string Description);

    private sealed record Prepared(Guid Id, MemoryLevel Level, MemoryType Type, Guid? ProjectId, string? Key, string? Name,
        string Label, string Description, Guid? SourceThreadId, DateTime CreatedAt, DateTime? PromotedAt, int HitCount,
        DateTime? LastAccessedAt, bool Orphaned, ImportedLabel? Labels, string? LegacyLabel)
    {
        public string Label { get; set; } = Label;
        public ImportedLabel? Labels { get; set; } = Labels;
    }

    private sealed record ImportedLabel(string Module, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind, string? Area, IReadOnlyList<string> Keywords,
        IReadOnlyList<string>? ExtraModules = null)
    {
        public string Composed => MemoryLabels.Compose(MemoryLabels.Display(Area, Module, Layers, Kind, ExtraModules), Keywords);
    }

    private sealed record LocalProject(Guid Id, string? Key, string Name)
    {
        public bool Owns(string? projectKey, Guid? projectId) =>
            (Key is not null && projectKey == Key) || (projectKey is null && projectId == Id);
    }

    private sealed class LocalProjects(IReadOnlyList<LocalProject> projects)
    {
        private readonly Dictionary<Guid, LocalProject> _byId = projects.ToDictionary(p => p.Id);

        // Several clones of one repository may be registered here; the first one stands for the key.
        private readonly Dictionary<string, LocalProject> _byKey = projects
            .Where(p => p.Key is not null)
            .GroupBy(p => p.Key!)
            .ToDictionary(g => g.Key, g => g.First());

        public LocalProject? Find(Guid? id) => id is { } value ? _byId.GetValueOrDefault(value) : null;

        public LocalProject? FindByKey(string key) => _byKey.GetValueOrDefault(key);
    }
}

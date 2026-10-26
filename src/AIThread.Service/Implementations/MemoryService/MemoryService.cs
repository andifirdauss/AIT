using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Project memory is scoped by Project.ProjectKey, so every clone of a repository (on any developer's machine, with
// any root folder) reads and writes the same memory. Rows written before keys existed carry only ProjectId and still
// match their own project. The project's business wiki pages are part of the planner's index too (MemoryType.Business),
// read-only here: they are written by the business wiki writer and in the Memory menu.
// Every memory is written with its structured label (MemoryLabels); memory from older versions stays readable as
// "unlabeled" until MemoryRelabelWorker relabels it.
public sealed partial class MemoryService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    SwitchableShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    IModuleRegistry registry,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AIThreadOptions> options,
    ILogger<MemoryService> logger) : IMemoryService
{
    private const int PlannerPageMaxChars = 15_000;

    // Change entries read from long memory per GetModuleMemoryAsync call: enough for the newest ones of a few modules.
    private const int MaxChangeRows = 300;

    // Characters of a change entry read for its title in the memory index.
    private const int TitleHeadChars = 200;

    // While the long memory database is down the planner keeps running without it; the activity log says so at most
    // this often instead of once per thread.
    private static readonly TimeSpan LongStoreNoticeInterval = TimeSpan.FromMinutes(10);
    private long _longStoreNoticeTicks;

    private sealed record PreparedLabel(string Module, IReadOnlyList<string> ExtraModules, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind, string? Area,
        IReadOnlyList<string> Keywords, string Composed);

    // Key is null for a project registered before keys existed and not seen since (the startup backfill fills it).
    private sealed record ProjectScope(Guid Id, string? Key, string Name)
    {
        public static ProjectScope Unknown(Guid id) => new(id, null, string.Empty);

        public bool Owns(string? projectKey, Guid? projectId) =>
            (Key is not null && projectKey == Key) || (projectKey is null && projectId == Id);
    }

    private sealed class LocalProjects(IReadOnlyList<ProjectScope> projects)
    {
        private readonly Dictionary<Guid, ProjectScope> _byId = projects.ToDictionary(p => p.Id);

        // Several clones of one repository may be registered here; the first one stands for the key.
        private readonly Dictionary<string, ProjectScope> _byKey = projects
            .Where(p => p.Key is not null)
            .GroupBy(p => p.Key!)
            .ToDictionary(g => g.Key, g => g.First());

        public ProjectScope? Find(Guid? id) => id is { } value ? _byId.GetValueOrDefault(value) : null;

        public ProjectScope? FindByKey(string key) => _byKey.GetValueOrDefault(key);
    }
}

using System.Collections.Concurrent;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IShortMemoryStore shortMemoryStore,
    LongMemoryStore longMemoryStore,
    IProjectMemoryKeys memoryKeys,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ProjectService> logger) : IProjectService
{
    // A session keeps its decision for a while so every prompt does not rescan the disk; ignored sessions are
    // re-checked after a few seconds, in case a marker (.git, a manifest) was added in the meantime.
    private static readonly TimeSpan SessionDecisionLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IgnoredSessionLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProfileLifetime = TimeSpan.FromSeconds(30);
    private const int MaxBrowseFolders = 1000;
    private const int MaxFoldersForProjectHint = 300;
    private const int MaxScanResults = 300;
    private const int MaxScanFolders = 50_000;
    private static readonly TimeSpan ScanTimeLimit = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, SessionDecision> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProjectProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    private sealed record SessionDecision(Guid? ProjectId, DateTime ExpiresAt);

    private sealed record ProjectProfile(IReadOnlyList<string> Stacks, IReadOnlyList<string> Modules, DateTime ExpiresAt);
}

using System.Collections.Concurrent;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace.Structure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

// Keeps the latest snapshot of the few projects viewed recently in memory; one scan at a time per project. The calls
// of functions (read by the tools node, FunctionCallScanner) come from the database.
public sealed partial class CodeStructureService(
    IProjectService projectService,
    IFunctionCallStore calls,
    IHostApplicationLifetime lifetime,
    ILogger<CodeStructureService> logger) : ICodeStructureService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1);
    private const int KeptProjects = 4;
    private const int MaxFolderFiles = 1_000;
    private const int MaxSearchHits = 100;
    private const int MinSearchLength = 2;

    private readonly ConcurrentDictionary<Guid, Entry> _projects = new();

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public volatile StructureSnapshot? Snapshot;
        public DateTime LastUsed { get; set; }

        // The background refresh in the queue (guarded by locking the entry).
        public CancellationTokenSource? Refresh { get; set; }
    }
}

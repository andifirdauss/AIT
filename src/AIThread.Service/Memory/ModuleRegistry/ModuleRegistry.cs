using System.Collections.Concurrent;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

// The modules of every project key (and of knowledge memory under MemoryLabels.KnowledgeKey). A name is resolved by
// name or alias ignoring case and separators, then by English plural; a merged module redirects to the module it was
// merged into. New modules are queued for the duplicate check (MemoryRelabelWorker drains the queue).
public sealed partial class ModuleRegistry(LongMemoryStore store, ILogger<ModuleRegistry> logger) : IModuleRegistry
{
    public const string CoreModule = "Core";

    private readonly ConcurrentQueue<(string Key, string Module)> _created = new();
    private readonly SemaphoreSlim _signal = new(0);
}

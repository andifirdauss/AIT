using System.Collections.Concurrent;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed record CodeMapHit(string Path, string Module, MemoryLayer Layer, string? Area, CodeMapSource Source);

// The modules and layers of a project's files, decided once and stored (CodeMapEntry) so the same file always gets the
// same answer. Lookup order: stored entry → rules (CodeMapHeuristics) when they are sure → the model, one call per batch
// of files grouped by name stem (the first batch alone, the rest a few at a time). A file being mapped by the model is
// mapped once: later callers wait for that call.
public sealed partial class CodeMapper(LongMemoryStore store, IModuleRegistry registry, IMemoryAi ai, ILogger<CodeMapper> logger) : ICodeMapper
{
    private const int GroupsPerCall = 40;
    private const int ParallelCalls = 3;
    private const int MaxTokens = 4_000;
    private const int LookupReferences = 10;
    private const int FilesPerReference = 3;

    private readonly ConcurrentDictionary<string, Task> _inflight = new(StringComparer.OrdinalIgnoreCase);

    private sealed record FileGroup(string Id, string Stem, List<string> Paths);
}

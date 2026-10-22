using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

// What one extraction did: the memories saved and the tokens spent; Failed when the model or the store failed.
public sealed record MemoryExtraction(int Saved, int Tokens, bool Failed = false);

// After a thread finishes, distills reusable project/knowledge memories into short memory, each with its full label.
// The files the thread touched are mapped first (waiting for the code map model when needed), so the module and layers
// of a memory come from the code map rather than from what the model happens to call them.
public sealed partial class MemoryExtractor(
    INodeCaller caller,
    IMemoryService memoryService,
    IProjectService projectService,
    ICodeMapper codeMapper,
    IModuleRegistry registry,
    IActivityLogService activityLog,
    ISecretGuard secrets,
    ILogger<MemoryExtractor> logger) : IMemoryExtractor
{
    private const int MaxMemories = 5;

    private sealed record ExtractedMemory(MemoryType Type, string Module, IReadOnlyList<string> OtherModules, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind,
        IReadOnlyList<string> Keywords, string Description);
}

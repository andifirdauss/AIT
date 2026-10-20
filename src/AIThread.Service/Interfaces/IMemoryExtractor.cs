using AIThread.Common.Entities;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IMemoryExtractor
{
    Task<MemoryExtraction> ExtractAsync(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken);
}

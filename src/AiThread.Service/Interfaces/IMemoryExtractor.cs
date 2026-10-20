using AiThread.Common.Entities;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface IMemoryExtractor
{
    Task<MemoryExtraction> ExtractAsync(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken);
}

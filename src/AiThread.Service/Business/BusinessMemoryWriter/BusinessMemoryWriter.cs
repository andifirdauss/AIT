using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

public sealed record BusinessBatchResult(IReadOnlyList<Guid> DigestedThreads, int PagesCreated, int PagesUpdated, int Images, IReadOnlyList<string> Warnings);

// Turns a batch of finished threads into business wiki changes: one call decides which pages the threads touch, then
// one call per page rewrites it. Threads are only marked as digested when every page they feed was saved, so a
// failure is retried with the next run.
public sealed partial class BusinessMemoryWriter(
    INineRouterService router,
    ISettingService settingService,
    INodeService nodeService,
    BusinessMemoryStore store,
    IBusinessPageStore pages,
    ILogger<BusinessMemoryWriter> logger) : IBusinessMemoryWriter
{
    private const int RouterMaxTokens = 2_000;
    private const int WriterMaxTokens = 8_000;

    private sealed record PageTarget(Guid? PageId, string Module, string Title, IReadOnlyList<string> Threads);

    private sealed record WriterAnswer(string Content, string? Summary, string? Labels, string? ChangeNote);
}

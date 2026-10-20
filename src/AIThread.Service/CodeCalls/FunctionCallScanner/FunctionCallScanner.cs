using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CodeCalls;

// Reads what functions call, at the smallest scale: only the functions whose lines the planner read or a thread
// changed. The tools node lists the calls of a batch of functions; every call it names must be written on (or next
// to) the line it gives, or it is dropped. Where each call is declared is resolved by AIT against the code structure
// (CallResolver), never by the model. A function whose body did not change since its last scan is skipped.
public sealed partial class FunctionCallScanner(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ICodeStructureService structure,
    IFunctionCallStore store,
    IProjectService projectService,
    INodeService nodeService,
    INodeCaller caller,
    ILogger<FunctionCallScanner> logger) : IFunctionCallScanner
{
    private const int MaxBatchChars = 16_000;
    private const int MaxBatchFunctions = 20;
    private const int MaxBodyChars = 8_000;
    private const int MaxFunctionsPerFile = 60;
    private const int MaxTokens = 4_000;
    private const int MaxTargetLength = 500;
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);

    private sealed record Pending(FunctionSpan Span, string Key, string Body)
    {
        public string Hash { get; } = FunctionSpans.Hash(Body);
    }
}

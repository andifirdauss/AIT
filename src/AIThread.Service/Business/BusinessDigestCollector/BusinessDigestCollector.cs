using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

// A finished thread as the business wiki writer reads it.
public sealed record ThreadDigest(Guid ThreadId, string Text, IReadOnlyList<ImageAttachment> Images);

// The threads of this machine that the business wiki has not seen yet, oldest first, for one project key (all local
// clones of the project together), then the commits made outside AIT that this machine's CommitIngestor summarized
// (IngestedCommit, shared store: only the AIT that claimed a commit hands it on, so it is written once).
public sealed partial class BusinessDigestCollector(IDbContextFactory<AIThreadDbContext> dbFactory, LongMemoryStore longStore,
    ILogger<BusinessDigestCollector> logger) : IBusinessDigestCollector
{
    public const int MaxThreadsPerBatch = 15;
    public const int MaxCharsPerBatch = 60_000;
    public const int MaxImagesPerBatch = 6;
    private const int MaxPromptChars = 4_000;
    private const int MaxReplyChars = 4_000;
    private const int MaxTasks = 30;
}

using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

// The business wiki per environment. A page of an environment is the newest revision of the working page whose source
// threads (every revision up to it) have all reached that environment (ChangePresence); a revision without threads (a
// manual edit) holds wherever the ones before it hold. A thread git knows nothing of (no ThreadChange: older, or not a
// repository) and a change thrown away or lost do not hold a page back. Pages are copied, never written by a model, so an
// environment page says exactly what the wiki said once. Compare shows two sides (environments or the working wiki) page
// by page, with the changes that are on one side only.
public sealed partial class BusinessEnvService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    BusinessMemoryStore store,
    LongMemoryStore longStore,
    IEventBus eventBus,
    ILogger<BusinessEnvService> logger) : IBusinessEnvService
{
    // The compare side that stands for the working wiki (an environment cannot be named so).
    public const string Working = "work";

    private const int MaxCompareChanges = 200;

    // One side of a compare: page id → what it shows.
    private sealed record SidePage(string Module, string Title, int Version, string Content);
}

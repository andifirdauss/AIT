using System.Collections.Concurrent;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

// Runs the business wiki writer over the threads waiting for it: for one project (button "Perbarui sekarang") or for
// every project (BusinessMemoryWorker). One run at a time on this machine; other developers' AiThread write to the
// same pages safely through the page version check.
public sealed partial class BusinessDigestRunner(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IBusinessDigestCollector collector,
    IBusinessMemoryWriter writer,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<BusinessDigestRunner> logger) : IBusinessDigestRunner
{
    // A run stops after this many batches; the rest waits for the next run.
    private const int MaxBatchesPerRun = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _running = new();
}

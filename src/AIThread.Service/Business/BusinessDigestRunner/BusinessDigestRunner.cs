using System.Collections.Concurrent;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

// Runs the business wiki writer over the threads waiting for it: for one project (button "Perbarui sekarang") or for
// every project (BusinessMemoryWorker). One run at a time on this machine; other developers' AIThread write to the
// same pages safely through the page version check.
public sealed partial class BusinessDigestRunner(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IBusinessDigestCollector collector,
    IBusinessMemoryWriter writer,
    IBusinessEnvService envPages,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<BusinessDigestRunner> logger) : IBusinessDigestRunner
{
    // A run stops after this many batches; the rest waits for the next run.
    private const int MaxBatchesPerRun = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _running = new();
}

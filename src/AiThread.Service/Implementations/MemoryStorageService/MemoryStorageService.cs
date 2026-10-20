using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    SwitchableShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    ILogger<MemoryStorageService> logger) : IMemoryStorageService
{
    private const int CopyBatchSize = 200;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.MemoryStore.v1");
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public static string DefaultSqlitePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread", "memory.db");
}

using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    SwitchableShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    ILogger<MemoryStorageService> logger) : IMemoryStorageService
{
    private const int CopyBatchSize = 200;

    // The purpose keeps the pre-rename "AiThread" spelling; a different purpose cannot read values already saved.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.MemoryStore.v1");
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public static string DefaultSqlitePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "memory.db");
}

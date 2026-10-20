using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    IModuleRegistry registry,
    IModuleMerger merger,
    ICodeMapper codeMapper,
    IMemoryRelabeler relabeler,
    IMemoryService memoryService,
    IProjectService projectService,
    ISettingService settingService,
    IHostApplicationLifetime lifetime,
    ILogger<MemoryCatalogService> logger) : IMemoryCatalogService
{
    private const int ScanMaxFiles = 3_000;
}

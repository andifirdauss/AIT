using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
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

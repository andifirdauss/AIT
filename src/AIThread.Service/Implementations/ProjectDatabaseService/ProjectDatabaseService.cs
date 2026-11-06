using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IDatabaseQueryService queries,
    IDatabaseBackupStore backups,
    IActivityLogService activityLog,
    ISettingService settingService,
    ILogger<ProjectDatabaseService> logger) : IProjectDatabaseService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.ProjectDatabases.v1");

    public const int MaxRowsLimit = 500;
}

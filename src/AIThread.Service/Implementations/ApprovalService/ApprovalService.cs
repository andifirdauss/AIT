using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

// Standing permissions are kept in this machine's database only: another developer's AIT asks its own developer.
public sealed partial class ApprovalService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ISettingService settingService,
    IActivityLogService activityLog,
    IEventBus eventBus) : IApprovalService
{
    private const string Category = "izin";
}

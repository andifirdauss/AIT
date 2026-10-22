using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

// The switches of the secret guard (kept in SystemSetting, applied to ISecretGuard at once) and its counters, plus
// whether the security & privacy notice was read.
public sealed partial class SecurityService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ISecretGuard secrets,
    IActivityLogService activityLog) : ISecurityService
{
}

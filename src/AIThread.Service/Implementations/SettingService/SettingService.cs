using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    ISecretGuard secrets,
    ILogger<SettingService> logger) : ISettingService
{
    // The purpose keeps the pre-rename "AiThread" spelling; a different purpose cannot read values already saved.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.v1");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile RuntimeSettings? _cache;
}

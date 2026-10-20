using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    ILogger<SettingService> logger) : ISettingService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.v1");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile RuntimeSettings? _cache;
}

using System.Security.Cryptography;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed class SettingService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    ILogger<SettingService> logger) : ISettingService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.v1");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile RuntimeSettings? _cache;

    public async Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is { } cached) return cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } again) return again;
            var entity = await LoadOrCreateAsync(cancellationToken);
            _cache = ToRuntime(entity);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var entity = await LoadOrCreateAsync(cancellationToken);
        return ToDto(entity, proxyBaseUrl);
    }

    public async Task<SystemSettingDto> UpdateAsync(UpdateSystemSettingRequest request, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var baseUrl = NormalizeRouterUrl(request.NineRouterBaseUrl);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppValidationException("URL 9router tidak valid. Contoh: http://localhost:20128");

        EnsureRange(request.PlannerMaxSteps, 3, 100, "Planner max steps");
        EnsureRange(request.ExecutorMaxRetries, 0, 5, "Executor max retries");
        EnsureRange(request.MaxParallelTasks, 1, 64, "Max parallel tasks");
        EnsureRange(request.RequestTimeoutSeconds, 30, 3600, "Request timeout");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is null)
        {
            entity = new SystemSetting { Id = SystemSetting.SingletonId };
            db.SystemSettings.Add(entity);
        }

        entity.NineRouterBaseUrl = baseUrl;
        if (request.ClearNineRouterApiKey)
            entity.NineRouterApiKeyProtected = null;
        else if (!string.IsNullOrWhiteSpace(request.NineRouterApiKey))
            entity.NineRouterApiKeyProtected = _protector.Protect(request.NineRouterApiKey.Trim());

        entity.ProxyApiKey = string.IsNullOrWhiteSpace(request.ProxyApiKey) ? null : request.ProxyApiKey.Trim();
        entity.PassthroughModel = string.IsNullOrWhiteSpace(request.PassthroughModel) ? null : request.PassthroughModel.Trim();
        entity.OrchestrationEnabled = request.OrchestrationEnabled;
        entity.StreamProgressToClient = request.StreamProgressToClient;
        entity.PlannerMaxSteps = request.PlannerMaxSteps;
        entity.ExecutorMaxRetries = request.ExecutorMaxRetries;
        entity.MaxParallelTasks = request.MaxParallelTasks;
        entity.RequestTimeoutSeconds = request.RequestTimeoutSeconds;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return ToDto(entity, proxyBaseUrl);
    }

    // Accepts what users typically paste from the 9router dashboard ("http://localhost:20128/v1").
    public static string NormalizeRouterUrl(string? url)
    {
        var value = (url ?? string.Empty).Trim().TrimEnd('/');
        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) value = value[..^3];
        return value.TrimEnd('/');
    }

    private async Task<SystemSetting> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is not null) return entity;

        entity = new SystemSetting { Id = SystemSetting.SingletonId };
        db.SystemSettings.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request created the row first.
            return await db.SystemSettings.AsNoTracking().FirstAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        }
        return entity;
    }

    private RuntimeSettings ToRuntime(SystemSetting entity) => new(
        entity.NineRouterBaseUrl,
        Unprotect(entity.NineRouterApiKeyProtected),
        entity.ProxyApiKey,
        entity.PassthroughModel,
        entity.OrchestrationEnabled,
        entity.StreamProgressToClient,
        entity.PlannerMaxSteps,
        entity.ExecutorMaxRetries,
        entity.MaxParallelTasks,
        entity.RequestTimeoutSeconds);

    private SystemSettingDto ToDto(SystemSetting entity, string proxyBaseUrl)
    {
        var key = Unprotect(entity.NineRouterApiKeyProtected);
        return new SystemSettingDto(
            entity.NineRouterBaseUrl,
            key is not null,
            Mask(key),
            entity.ProxyApiKey,
            entity.PassthroughModel,
            entity.OrchestrationEnabled,
            entity.StreamProgressToClient,
            entity.PlannerMaxSteps,
            entity.ExecutorMaxRetries,
            entity.MaxParallelTasks,
            entity.RequestTimeoutSeconds,
            proxyBaseUrl);
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            // Data Protection keys were rotated or deleted; the key has to be entered again.
            logger.LogWarning(ex, "Stored 9router API key can no longer be decrypted.");
            return null;
        }
    }

    private static string? Mask(string? key) =>
        key is null ? null : key.Length <= 10 ? new string('•', key.Length) : $"{key[..6]}••••{key[^4..]}";

    private static void EnsureRange(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new AppValidationException($"{name} harus di antara {min} dan {max}.");
    }
}

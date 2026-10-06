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
        EnsureRange(request.BuildTimeoutSeconds, 30, 3600, "Build timeout");
        EnsureRange(request.MaxFixRounds, 0, 5, "Putaran perbaikan");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);

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
        entity.BuildEnabled = request.BuildEnabled;
        entity.BuildTimeoutSeconds = request.BuildTimeoutSeconds;
        entity.MaxFixRounds = request.MaxFixRounds;
        entity.PlannerCanAsk = request.PlannerCanAsk;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return ToDto(entity, proxyBaseUrl);
    }

    public async Task<TelegramSettingsDto> GetTelegramAsync(CancellationToken cancellationToken = default) =>
        ToTelegramDto(await LoadOrCreateAsync(cancellationToken));

    public async Task<TelegramSettingsDto> UpdateTelegramAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);

        if (request.ClearBotToken)
            entity.TelegramBotTokenProtected = null;
        else if (!string.IsNullOrWhiteSpace(request.BotToken))
            entity.TelegramBotTokenProtected = _protector.Protect(request.BotToken.Trim());

        if (request.Enabled && Unprotect(entity.TelegramBotTokenProtected) is null)
            throw new AppValidationException("Isi bot token dari @BotFather sebelum mengaktifkan Telegram.");

        entity.TelegramEnabled = request.Enabled;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return ToTelegramDto(entity);
    }

    public async Task<RuntimeSettings> UpdateSkillsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default)
    {
        EnsureRange(request.MaxPromptChars, 4_000, 200_000, "Batas karakter skill per node");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.SkillOrchestratorModel = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.SkillPromptMaxChars = request.MaxPromptChars;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
    }

    public async Task<RuntimeSettings> UpdateBusinessAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        EnsureRange(request.IntervalHours, 1, 168, "Interval business memory (jam)");
        if (request.DeveloperName?.Trim().Length > 200) throw new AppValidationException("Nama developer maksimal 200 karakter.");
        if (request.Model?.Trim().Length > 200) throw new AppValidationException("Model maksimal 200 karakter.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.BusinessMemoryEnabled = request.Enabled;
        entity.BusinessMemoryIntervalHours = request.IntervalHours;
        entity.BusinessMemoryModel = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.DeveloperName = string.IsNullOrWhiteSpace(request.DeveloperName) ? null : request.DeveloperName.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
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

    private static async Task<SystemSetting> LoadTrackedAsync(AiThreadDbContext db, CancellationToken cancellationToken)
    {
        var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is not null) return entity;

        entity = new SystemSetting { Id = SystemSetting.SingletonId };
        db.SystemSettings.Add(entity);
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
        entity.RequestTimeoutSeconds,
        entity.TelegramEnabled,
        Unprotect(entity.TelegramBotTokenProtected),
        entity.BuildEnabled,
        entity.BuildTimeoutSeconds,
        entity.MaxFixRounds,
        entity.PlannerCanAsk,
        entity.SkillOrchestratorModel,
        entity.SkillPromptMaxChars,
        entity.BusinessMemoryEnabled,
        entity.BusinessMemoryIntervalHours,
        entity.BusinessMemoryModel,
        entity.DeveloperName);

    private TelegramSettingsDto ToTelegramDto(SystemSetting entity)
    {
        var token = Unprotect(entity.TelegramBotTokenProtected);
        return new TelegramSettingsDto(entity.TelegramEnabled, token is not null, Mask(token));
    }

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
            entity.BuildEnabled,
            entity.BuildTimeoutSeconds,
            entity.MaxFixRounds,
            proxyBaseUrl,
            entity.PlannerCanAsk);
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
            // Data Protection keys were rotated or deleted; the secret has to be entered again.
            logger.LogWarning(ex, "A stored secret (9router API key or Telegram bot token) can no longer be decrypted.");
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

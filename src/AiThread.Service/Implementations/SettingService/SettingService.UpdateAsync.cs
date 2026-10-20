using AiThread.Common.DTOs;
using AiThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
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
}

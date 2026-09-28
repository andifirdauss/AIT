using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

public interface ISettingService
{
    Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default);

    Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default);

    Task<SystemSettingDto> UpdateAsync(UpdateSystemSettingRequest request, string proxyBaseUrl, CancellationToken cancellationToken = default);
}

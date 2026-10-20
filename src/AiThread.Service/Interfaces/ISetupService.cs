using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// The Setup menu: the first-run steps computed from the live state, and the actions that complete them in place.
public interface ISetupService
{
    // refreshRouter: probe 9router now instead of using the monitor's last status.
    Task<SetupStatusDto> GetStatusAsync(string proxyBaseUrl, bool refreshRouter = false, CancellationToken cancellationToken = default);

    Task<SetupRouterResultDto> SaveRouterAsync(SetupRouterRequest request, CancellationToken cancellationToken = default);

    Task<SetupPresetResultDto> ApplyPresetAsync(SetupPresetRequest request, CancellationToken cancellationToken = default);
}

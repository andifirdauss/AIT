using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Starts a local 9router when it is offline, installing it with npm when it is missing.
public interface INineRouterLauncher
{
    // manual: the user asked (dashboard button), so installing is always allowed. Otherwise installing follows
    // AIThread:NineRouter:AutoInstall and is tried once per process.
    Task<RouterLaunchResultDto> EnsureRunningAsync(bool manual, CancellationToken cancellationToken = default);

    // Stops the 9router this process started; does nothing for one the user started.
    void StopStarted();
}

using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

public interface IAppLauncher
{
    // Starts the app and waits until baseUrl answers. App is the running process (dispose it to stop it); Error says
    // why it did not come up (it exited, did not answer in time, could not start), with the end of its output.
    Task<AppStartResult> StartAsync(AppLaunchCommand command, string baseUrl, CancellationToken cancellationToken);
}

using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

public interface ITestBrowserLauncher
{
    // profile: a name of its own (a test account of a project) for a browser profile kept apart from the shared one;
    // null: the shared profile.
    Task<ITestBrowser> LaunchAsync(Func<string, Task> notice, CancellationToken cancellationToken, string? profile = null);
}

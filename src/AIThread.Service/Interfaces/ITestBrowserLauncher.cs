using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

public interface ITestBrowserLauncher
{
    Task<ITestBrowser> LaunchAsync(Func<string, Task> notice, CancellationToken cancellationToken);
}

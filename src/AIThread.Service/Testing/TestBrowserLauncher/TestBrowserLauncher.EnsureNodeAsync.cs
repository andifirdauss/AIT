using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // Points the Playwright driver at a Node.js through PLAYWRIGHT_NODEJS_PATH, which it reads on every start: one set
    // already, the driver's own node.exe (development builds), Node.js on PATH, an earlier download, or a new download.
    private async Task EnsureNodeAsync(Func<string, Task> notice, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(NodePathVariable))) return;
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, ".playwright", "node", "win32_x64", "node.exe"))) return;

        var node = await FindInstalledNodeAsync(cancellationToken);
        if (node is null && File.Exists(DownloadedNode)) node = DownloadedNode;
        node ??= await DownloadNodeAsync(notice, cancellationToken);

        logger.LogInformation("Playwright driver uses Node.js at {Path}", node);
        Environment.SetEnvironmentVariable(NodePathVariable, node);
    }
}

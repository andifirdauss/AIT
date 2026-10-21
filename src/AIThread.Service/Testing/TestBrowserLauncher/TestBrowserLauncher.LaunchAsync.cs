using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // notice: progress text for the developer (waiting for another thread's test, downloading Chromium).
    public async Task<ITestBrowser> LaunchAsync(Func<string, Task> notice, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            await notice("browser tester sedang dipakai thread lain — menunggu giliran");
            await _gate.WaitAsync(cancellationToken);
        }

        IPlaywright? playwright = null;
        IBrowserContext? context = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            var failures = new List<string>();
            foreach (var (channel, name) in Browsers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context = await TryLaunchAsync(playwright, channel, failures);
                if (context is null && channel is null && await InstallChromiumAsync(notice, failures, cancellationToken))
                    context = await TryLaunchAsync(playwright, channel, failures);
                if (context is null) continue;

                context.SetDefaultTimeout(ActionTimeoutMs);
                var browser = await PlaywrightTestBrowser.CreateAsync(playwright, context, name, () => _gate.Release());
                logger.LogInformation("Tester browser opened: {Browser}", name);
                return browser;
            }
            throw new InvalidOperationException($"Browser tester tidak bisa dibuka ({string.Join("; ", failures)}).");
        }
        catch
        {
            if (context is not null) await CloseQuietlyAsync(context);
            playwright?.Dispose();
            _gate.Release();
            throw;
        }
    }
}

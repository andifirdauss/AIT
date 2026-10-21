using Microsoft.Playwright;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // Null when this browser is not installed or does not start; the reason goes to failures.
    private static async Task<IBrowserContext?> TryLaunchAsync(IPlaywright playwright, string? channel, List<string> failures)
    {
        try
        {
            return await playwright.Chromium.LaunchPersistentContextAsync(Path.Combine(ProfileRoot, channel ?? "chromium"),
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = channel,
                    SlowMo = SlowMoMs,
                    // Only localhost is tested, often over the development certificate.
                    IgnoreHTTPSErrors = true
                });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            failures.Add($"{channel ?? "chromium"}: {ex.Message.ReplaceLineEndings(" ").Split("Call log", 2)[0].Trim()}");
            return null;
        }
    }
}

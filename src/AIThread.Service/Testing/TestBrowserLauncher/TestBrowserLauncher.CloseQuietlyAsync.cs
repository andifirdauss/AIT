using Microsoft.Playwright;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    private static async Task CloseQuietlyAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Closing after a failed start; nothing more to do.
        }
    }
}

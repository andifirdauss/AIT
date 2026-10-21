using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // Downloads Playwright's Chromium (once, into %LOCALAPPDATA%\ms-playwright) when the last failure says it is missing.
    private async Task<bool> InstallChromiumAsync(Func<string, Task> notice, List<string> failures, CancellationToken cancellationToken)
    {
        if (failures.LastOrDefault()?.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) != true) return false;

        await notice("Edge/Chrome tidak ditemukan — mengunduh Chromium untuk Playwright (sekali saja, ±150 MB)");
        var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]), cancellationToken);
        if (exitCode == 0) return true;

        logger.LogWarning("playwright install chromium exited with {ExitCode}", exitCode);
        failures.Add($"unduh Chromium gagal (exit {exitCode})");
        return false;
    }
}

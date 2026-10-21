using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

// Opens the visible tester browser with Playwright: Edge first (it comes with Windows), then Chrome, then Playwright's
// own Chromium, downloaded once when neither is installed. Each browser keeps its own profile under
// %LOCALAPPDATA%\AIThread\tester-browser, so a login the developer makes there survives the next test runs.
public sealed partial class TestBrowserLauncher(ILogger<TestBrowserLauncher> logger) : ITestBrowserLauncher
{
    // Slow enough for the developer to follow what the tester does.
    private const float SlowMoMs = 250;
    private const float ActionTimeoutMs = 10_000;

    // Channel null = Playwright's bundled Chromium.
    private static readonly (string? Channel, string Name)[] Browsers =
        [("msedge", "Microsoft Edge"), ("chrome", "Google Chrome"), (null, "Chromium (Playwright)")];

    // One tester browser at a time: a profile can be open only once, and two windows driven at once would confuse the
    // developer watching.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string ProfileRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "tester-browser");
}

using System.Diagnostics;
using AIThread.Common.Options;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Runs 9router the way its own "Hide to Tray" does (node cli.js --tray), in a hidden console whose output goes to
// %LOCALAPPDATA%\AIThread\logs\9router.log. On start 9router kills whatever holds its port, so it is only started
// when that port is free.
public sealed partial class NineRouterLauncher(
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AIThreadOptions> options,
    ILogger<NineRouterLauncher> logger) : INineRouterLauncher, IDisposable
{
    private const string Category = "9router";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // Per process, not per host: the exe's menu rebuilds the host when the port changes.
    private static int _autoInstallTried;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _started;

    private NineRouterLaunchOptions Settings => options.Value.NineRouter;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "logs", "9router.log");
}

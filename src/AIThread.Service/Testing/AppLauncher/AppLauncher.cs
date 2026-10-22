using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

// App: the app AIT started, null when it did not come up (Error says why in one sentence, Output holds the last lines it
// printed) or when another process answered at the URL while it was starting (both null).
public sealed record AppStartResult(RunningApp? App, string? Error, string? Output = null);

// Starts the app of a test that is not running yet (AppLaunchResolver picks the command) and waits until its URL answers.
// dotnet run builds first and a dev server compiles on start, so the wait is generous; the output (without build
// warnings) is kept only for the error message.
public sealed partial class AppLauncher(ILogger<AppLauncher> logger) : IAppLauncher
{
    private const int TailLines = 15;
    private const int MaxTailChars = 1_500;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
}

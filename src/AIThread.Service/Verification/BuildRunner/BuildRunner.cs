using System.Collections.Concurrent;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

public sealed record BuildResult(
    string Command,
    bool Succeeded,
    bool TimedOut,
    int? ExitCode,
    IReadOnlyList<BuildDiagnostic> Errors,
    int WarningCount,
    string OutputTail,
    long DurationMs,
    string? StartError = null)
{
    // Every error is an output copy blocked by a running program; the code compiled.
    public bool OnlyLockedOutput => !Succeeded && !TimedOut && StartError is null && Errors.Count > 0 && Errors.All(e => e.IsLockedOutput);

    // Good enough to stop fixing: the build succeeded or only failed to overwrite locked binaries.
    public bool Passed => Succeeded || OnlyLockedOutput;

    // The output file a running program held before the build, which therefore ran without its clean step.
    public string? InUseOutput { get; init; }
}

// Runs a BuildCommand: one build per project root at a time (two builds would fight over obj/), stopped together
// with its child processes on timeout or cancellation. Diagnostics are parsed while the output streams, so a long
// log cannot push the errors out of memory.
public sealed partial class BuildRunner(ILogger<BuildRunner> logger) : IBuildRunner
{
    private const int TailLines = 120;
    private const int MaxToolchainMessage = 300;

    // bin\<configuration>\<framework>\<runtime>\ and bin\roslyn\ are deep enough; a huge bin is not worth a full scan.
    private const int InUseDepth = 4;
    private const int MaxInUseChecks = 5000;

    // Build servers started by the build (MSBuild nodes, the compiler server) can inherit the output pipes and keep
    // them open after the build exits; whatever the build wrote is read by then.
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
}

using AIThread.Service.Interfaces;

namespace AIThread.Service.Workspace.Commands;

// What one run_command produced. Output holds stdout then stderr, already cut to what a tool result can hold;
// StartError is set when the shell could not start at all.
public sealed record CommandResult(
    string Command,
    string WorkingDirectory,
    string Shell,
    int? ExitCode,
    bool TimedOut,
    bool Truncated,
    string Output,
    long DurationMs,
    string? StartError = null)
{
    public bool Succeeded => StartError is null && !TimedOut && ExitCode == 0;
}

// Runs the planner's commands through bash (Git Bash on Windows, so the same command line works on every machine),
// or cmd.exe when no bash is found. The command was checked by CommandPolicy, or allowed by the developer. stdin is
// closed, nothing asks for credentials, and the whole process tree is stopped on timeout.
public sealed partial class CommandRunner : ICommandRunner
{
    public const int MaxOutputChars = 20_000;

    private static readonly Lazy<string?> Bash = new(FindBash);
}

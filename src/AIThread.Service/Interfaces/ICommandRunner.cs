using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Interfaces;

// Runs a shell command for the planner's run_command tool (after CommandPolicy or the developer allowed it).
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default);
}

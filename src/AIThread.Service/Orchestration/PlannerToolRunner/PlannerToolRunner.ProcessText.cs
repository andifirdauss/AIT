using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // What process_output and stop_process tell the planner: the state, then the last lines with secrets masked.
    private string ProcessText(BackgroundOutput output, PlannerInput input, bool stopped)
    {
        var age = $"{output.Age.TotalSeconds:0}s";
        var exit = output.ExitCode?.ToString() ?? "?";
        var status = stopped
            ? output.Running ? $"Stopped process {output.Id} after {age}." : $"Process {output.Id} had already ended (exit code {exit}); it is forgotten now."
            : output.Running ? $"Process {output.Id} is running ({age})."
            : output.Expired ? $"Process {output.Id} was stopped by AIT after {BackgroundProcesses.MaxAge.TotalMinutes:0} minutes."
            : $"Process {output.Id} exited with code {exit}.";
        var text = output.Output.Trim().Length == 0 ? "(no output)" : secrets.RedactFile(".env", output.Output, input.Scope.Secrets.Keys);
        return $"{status}\n{(output.Truncated ? "Last lines (earlier output left out):" : "Output:")}\n{text}";
    }
}

using System.Text;

namespace AiThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    // Small on purpose: the tools node finds the code itself, the planner's brief is not repeated.
    private static string Brief(PlannerInput input, string question)
    {
        var project = input.Project;
        var builder = new StringBuilder();
        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        if (project.Stacks.Count > 0) builder.Append($"Stack: {string.Join(", ", project.Stacks)}\n");
        builder.Append("Tool paths are relative to the root. The read tools also accept absolute paths outside the root (read-only).\n\n");
        builder.Append("# Question from the planner\n");
        builder.Append(question.Trim()).Append('\n');
        return builder.ToString();
    }
}

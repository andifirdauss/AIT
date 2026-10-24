using System.Text;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    // Small on purpose: the planner's brief is not repeated. The folder map saves the turns a tools node otherwise spends
    // on list_directory and find_files just to learn where code lives. Everything before the question is the same for
    // every investigation of the project, so providers that cache prompt prefixes reuse it.
    private static string Brief(PlannerInput input, WorkspaceTools tools, string question)
    {
        var project = input.Project;
        var builder = new StringBuilder();
        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        if (project.Stacks.Count > 0) builder.Append($"Stack: {string.Join(", ", project.Stacks)}\n");
        if (project.Modules.Count > 0)
        {
            builder.Append("Manifests found (solution, build and package files):\n");
            foreach (var module in project.Modules.Take(60)) builder.Append("- ").Append(module).Append('\n');
        }
        builder.Append("Tool paths are relative to the root. The read tools also accept absolute paths outside the root (read-only).\n\n");

        builder.Append($"## Folders (depth {MapDepth}, files not listed)\n");
        builder.Append(tools.ListDirectory(".", MapDepth, MaxMapEntries, directoriesOnly: true)).Append("\n\n");

        builder.Append("# Question from the planner\n");
        builder.Append(question.Trim()).Append('\n');
        return builder.ToString();
    }
}

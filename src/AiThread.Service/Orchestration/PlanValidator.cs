using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Normalizes the plan in place and returns problems phrased for the planner to fix.
public static class PlanValidator
{
    public const int MaxTasks = 40;

    public static IReadOnlyList<string> Validate(ThreadPlan plan, WorkspaceScope scope)
    {
        var errors = new List<string>();
        plan.Summary = plan.Summary?.Trim() ?? string.Empty;
        plan.Contract = plan.Contract.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();

        if (plan.Tasks.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(plan.Answer) && string.IsNullOrWhiteSpace(plan.Summary))
                errors.Add("The plan has no tasks and no answer. Add tasks, or put the full answer in \"answer\".");
            return errors;
        }

        if (plan.Tasks.Count > MaxTasks)
            errors.Add($"The plan has {plan.Tasks.Count} tasks; the maximum is {MaxTasks}. Merge related changes.");
        if (string.IsNullOrWhiteSpace(plan.Summary))
            errors.Add("\"summary\" is required.");

        for (var i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            task.Id = string.IsNullOrWhiteSpace(task.Id) ? $"T{i + 1}" : task.Id.Trim();
            task.Action = string.IsNullOrWhiteSpace(task.Action) ? "edit" : task.Action.Trim().ToLowerInvariant();
            task.File = (task.File ?? string.Empty).Trim().Replace('\\', '/');
            task.Title = string.IsNullOrWhiteSpace(task.Title) ? task.File : task.Title.Trim();
            task.DependsOn = task.DependsOn?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var duplicateIds = plan.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0) errors.Add($"Duplicate task ids: {string.Join(", ", duplicateIds)}.");

        var ids = new HashSet<string>(plan.Tasks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var createTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in plan.Tasks)
        {
            var label = $"Task {task.Id}";
            if (string.IsNullOrWhiteSpace(task.Instruction)) errors.Add($"{label}: \"instruction\" is empty.");
            if (task.Action is not ("edit" or "create")) errors.Add($"{label}: action must be \"edit\" or \"create\", not \"{task.Action}\".");

            if (!scope.TryResolve(task.File, out var fullPath, out var pathError))
            {
                errors.Add($"{label}: {pathError} Tasks can only create or edit files inside the project root; put anything about other places in \"answer\" or \"summary\".");
            }
            else if (scope.IsIgnoredPath(scope.ToRelative(fullPath)))
            {
                errors.Add($"{label}: '{task.File}' is inside a generated or third-party folder and must not be edited.");
            }
            else if (Directory.Exists(fullPath))
            {
                errors.Add($"{label}: '{task.File}' is a directory; tasks must target a file.");
            }
            else
            {
                task.File = scope.ToRelative(fullPath);
                var exists = File.Exists(fullPath);
                if (task.Action == "edit" && !exists)
                    errors.Add($"{label}: '{task.File}' does not exist. Use action \"create\" for new files, or fix the path (use find_files).");
                if (task.Action == "create" && exists)
                    errors.Add($"{label}: '{task.File}' already exists. Use action \"edit\".");
                if (task.Action == "create" && !createTargets.Add(task.File))
                    errors.Add($"{label}: another task already creates '{task.File}'. Merge them into one task.");
            }

            foreach (var dependency in task.DependsOn ?? [])
            {
                if (string.Equals(dependency, task.Id, StringComparison.OrdinalIgnoreCase)) errors.Add($"{label}: depends on itself.");
                else if (!ids.Contains(dependency)) errors.Add($"{label}: depends on unknown task '{dependency}'.");
            }
        }

        if (errors.Count == 0 && HasCycle(plan.Tasks)) errors.Add("dependsOn contains a cycle.");
        return errors;
    }

    // Kahn's algorithm: a cycle leaves nodes that never reach in-degree zero.
    private static bool HasCycle(List<PlanTask> tasks)
    {
        var inDegree = tasks.ToDictionary(t => t.Id, t => t.DependsOn?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        var dependents = tasks.ToDictionary(t => t.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
            foreach (var dependency in task.DependsOn ?? [])
                dependents[dependency].Add(task.Id);

        var ready = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            visited++;
            foreach (var dependent in dependents[id])
                if (--inDegree[dependent] == 0) ready.Enqueue(dependent);
        }
        return visited != tasks.Count;
    }
}

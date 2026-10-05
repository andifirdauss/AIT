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
            task.Action = string.IsNullOrWhiteSpace(task.Action) ? PlanTask.Edit : task.Action.Trim().ToLowerInvariant();
            task.File = (task.File ?? string.Empty).Trim().Replace('\\', '/');
            task.To = string.IsNullOrWhiteSpace(task.To) ? null : task.To.Trim().Replace('\\', '/');
            task.Title = string.IsNullOrWhiteSpace(task.Title) ? task.File : task.Title.Trim();
            task.DependsOn = task.DependsOn?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var duplicateIds = plan.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0) errors.Add($"Duplicate task ids: {string.Join(", ", duplicateIds)}.");

        var ids = new HashSet<string>(plan.Tasks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        // Paths that appear during the run (create targets and move destinations), and paths that disappear (delete and
        // move sources), each with the task responsible.
        var arrivals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingEdits = new List<PlanTask>();

        foreach (var task in plan.Tasks)
        {
            var label = $"Task {task.Id}";
            if (task.Action is not (PlanTask.Edit or PlanTask.Create or PlanTask.Delete or PlanTask.Move))
                errors.Add($"{label}: action must be \"edit\", \"create\", \"delete\" or \"move\", not \"{task.Action}\".");
            if (string.IsNullOrWhiteSpace(task.Instruction) && !task.IsFileOperation) errors.Add($"{label}: \"instruction\" is empty.");

            if (Resolve(task.File, label, scope, errors) is { } fullPath)
            {
                task.File = scope.ToRelative(fullPath);
                var exists = File.Exists(fullPath);
                if (task.Action == PlanTask.Edit && !exists) missingEdits.Add(task);
                if (task.IsCreate && exists)
                    errors.Add($"{label}: '{task.File}' already exists. Use action \"edit\".");
                if (task.IsCreate && !arrivals.TryAdd(task.File, task.Id))
                    errors.Add($"{label}: another task already creates or moves a file to '{task.File}'. Merge them into one task.");
                if (task.IsFileOperation && !exists)
                    errors.Add($"{label}: '{task.File}' does not exist, so it cannot be {(task.IsMove ? "moved" : "deleted")}. Fix the path (use find_files).");
                if (task.IsFileOperation && !removals.TryAdd(task.File, task.Id))
                    errors.Add($"{label}: another task already deletes or moves '{task.File}'.");
            }

            if (task.IsMove) ValidateDestination(task, label, scope, arrivals, errors);

            if (task.IsFileOperation && string.IsNullOrWhiteSpace(task.Instruction))
                task.Instruction = task.IsMove ? $"Move {task.File} to {task.To}." : $"Delete {task.File}.";

            foreach (var dependency in task.DependsOn ?? [])
            {
                if (string.Equals(dependency, task.Id, StringComparison.OrdinalIgnoreCase)) errors.Add($"{label}: depends on itself.");
                else if (!ids.Contains(dependency)) errors.Add($"{label}: depends on unknown task '{dependency}'.");
            }
        }

        // A file that is deleted or moved away must not be touched by any other task; a moved file is edited at its new
        // path, after the move.
        foreach (var task in plan.Tasks)
        {
            if (removals.TryGetValue(task.File, out var remover) && !string.Equals(remover, task.Id, StringComparison.OrdinalIgnoreCase))
            {
                var mover = plan.Tasks.First(t => string.Equals(t.Id, remover, StringComparison.OrdinalIgnoreCase));
                errors.Add(mover.IsMove
                    ? $"Task {task.Id}: '{task.File}' is moved to '{mover.To}' by task {remover}. Target '{mover.To}' instead and add \"{remover}\" to dependsOn."
                    : $"Task {task.Id}: '{task.File}' is deleted by task {remover}. Drop one of the two tasks.");
            }
        }

        foreach (var task in missingEdits)
        {
            var label = $"Task {task.Id}";
            if (!arrivals.TryGetValue(task.File, out var arrival) || !plan.Tasks.Any(t => t.IsMove && string.Equals(t.Id, arrival, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{label}: '{task.File}' does not exist. Use action \"create\" for new files, or fix the path (use find_files).");
            else if (!(task.DependsOn ?? []).Contains(arrival, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{label}: '{task.File}' only exists after task {arrival} moves it there. Add \"{arrival}\" to dependsOn.");
        }

        if (errors.Count == 0 && HasCycle(plan.Tasks)) errors.Add("dependsOn contains a cycle.");
        return errors;
    }

    // Full path of a file a task may change, or null after adding the reason it may not.
    private static string? Resolve(string path, string label, WorkspaceScope scope, List<string> errors)
    {
        if (!scope.TryResolve(path, out var fullPath, out var pathError))
            errors.Add($"{label}: {pathError} Tasks can only change files inside the project root; put anything about other places in \"answer\" or \"summary\".");
        else if (scope.IsIgnoredPath(scope.ToRelative(fullPath)))
            errors.Add($"{label}: '{path}' is inside a generated or third-party folder and must not be changed.");
        else if (Directory.Exists(fullPath))
            errors.Add($"{label}: '{path}' is a directory; tasks must target a file (move or delete each file of a folder in its own task).");
        else
            return fullPath;
        return null;
    }

    private static void ValidateDestination(PlanTask task, string label, WorkspaceScope scope, Dictionary<string, string> arrivals, List<string> errors)
    {
        if (task.To is null)
        {
            errors.Add($"{label}: action \"move\" needs \"to\", the destination path.");
            return;
        }
        if (Resolve(task.To, label, scope, errors) is not { } fullPath) return;

        task.To = scope.ToRelative(fullPath);
        // Paths compare without case (Windows, and the snapshot table), so a rename that only changes case is refused.
        if (string.Equals(task.To, task.File, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{label}: \"to\" must be a different path than \"file\" (renames that only change letter case are not supported).");
        else if (File.Exists(fullPath))
            errors.Add($"{label}: the destination '{task.To}' already exists. Choose another destination, or edit the existing file instead.");
        else if (!arrivals.TryAdd(task.To, task.Id))
            errors.Add($"{label}: another task already creates or moves a file to '{task.To}'.");
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

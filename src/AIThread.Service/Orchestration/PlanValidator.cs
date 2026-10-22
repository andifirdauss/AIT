using AIThread.Service.Testing;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

// What the unit test part of a plan is checked against: the catalog's active test numbers ("UT-0012") with their test
// file, and whether new tests may be added.
public sealed record UnitTestRules(IReadOnlyDictionary<string, string> Known, bool AllowNew);

// Normalizes the plan in place and returns problems phrased for the planner to fix.
public static class PlanValidator
{
    public const int MaxTasks = 40;
    public const int MaxTestScenarios = 6;
    public const int MaxNewTests = 15;

    private static readonly string[] Requirements = [TestEnvironment.Database, TestEnvironment.Network, TestEnvironment.Config];

    // requireTest: Auto Testing is on, so a plan that changes files must say how to test it. unitTests: the project has
    // a unit test catalog; null drops the plan's tests section.
    public static IReadOnlyList<string> Validate(ThreadPlan plan, WorkspaceScope scope, bool requireTest = false, UnitTestRules? unitTests = null)
    {
        var errors = new List<string>();
        plan.Summary = plan.Summary?.Trim() ?? string.Empty;
        plan.Contract = plan.Contract.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();

        if (plan.Tasks.Count == 0)
        {
            // Nothing changes. The developer may still want the existing behaviour tested: on the running app (a
            // runnable test section) and/or by unit tests (tests.run).
            if (requireTest && plan.Test is { } wanted && !string.Equals(wanted.Level?.Trim(), TestPlan.None, StringComparison.OrdinalIgnoreCase))
                ValidateTest(plan, requireTest, errors);
            else
                plan.Test = null;
            ValidateTestRun(plan, unitTests, errors);
            if (string.IsNullOrWhiteSpace(plan.Answer) && string.IsNullOrWhiteSpace(plan.Summary))
                errors.Add("The plan has no tasks and no answer. Add tasks, or put the full answer in \"answer\".");
            return errors;
        }
        ValidateTest(plan, requireTest, errors);

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
            task.Members = task.Members?.Where(m => m is not null).Select(CSharpMemberMover.NormalizeName).Where(m => m.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            task.Title = string.IsNullOrWhiteSpace(task.Title) ? task.File : task.Title.Trim();
            task.DependsOn = task.DependsOn?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var duplicateIds = plan.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0) errors.Add($"Duplicate task ids: {string.Join(", ", duplicateIds)}.");

        var ids = new HashSet<string>(plan.Tasks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var byId = plan.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Paths that appear during the run (create targets and move destinations), and paths that disappear (delete and
        // move sources), each with the task responsible.
        var arrivals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingEdits = new List<PlanTask>();

        foreach (var task in plan.Tasks)
        {
            var label = $"Task {task.Id}";
            if (task.Action is not (PlanTask.Edit or PlanTask.Create or PlanTask.Delete or PlanTask.Move or PlanTask.Extract))
                errors.Add($"{label}: action must be \"edit\", \"create\", \"delete\", \"move\" or \"extract\", not \"{task.Action}\".");
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
                    errors.Add($"{label}: '{task.File}' does not exist, so it cannot be {(task.IsMove ? "moved" : task.IsExtract ? "extracted from" : "deleted")}. Fix the path (use find_files).");
                if ((task.IsDelete || task.IsMove) && !removals.TryAdd(task.File, task.Id))
                    errors.Add($"{label}: another task already deletes or moves '{task.File}'.");
            }

            if (task.IsMove) ValidateDestination(task, label, scope, arrivals, errors);
            if (task.IsExtract) ValidateExtract(task, label, scope, arrivals, byId, errors);

            if (task.IsExtract)
            {
                // The instruction is what people see on the thread page; it always names the members.
                var move = $"Move {string.Join(", ", task.Members ?? [])} from {task.File} to {task.To}.";
                task.Instruction = string.IsNullOrWhiteSpace(task.Instruction) || task.Instruction.StartsWith(move, StringComparison.Ordinal)
                    ? move
                    : $"{move} {task.Instruction.Trim()}";
            }
            else if (task.IsFileOperation && string.IsNullOrWhiteSpace(task.Instruction))
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
            if (!arrivals.TryGetValue(task.File, out var arrival) || !plan.Tasks.Any(t => (t.IsMove || t.IsExtract) && string.Equals(t.Id, arrival, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{label}: '{task.File}' does not exist. Use action \"create\" for new files, or fix the path (use find_files).");
            else if (!(task.DependsOn ?? []).Contains(arrival, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{label}: '{task.File}' only exists after task {arrival} {(byId[arrival].IsExtract ? "extracts members into it" : "moves it there")}. Add \"{arrival}\" to dependsOn.");
        }

        if (errors.Count == 0 && HasCycle(plan.Tasks)) errors.Add("dependsOn contains a cycle.");
        ValidateUnitTests(plan, unitTests, errors);
        return errors;
    }

    // Decisions about registered tests and the new tests of a plan that changes files.
    private static void ValidateUnitTests(ThreadPlan plan, UnitTestRules? rules, List<string> errors)
    {
        if (rules is null || plan.Tests is not { } tests)
        {
            plan.Tests = null;
            return;
        }

        tests.Run = [];
        var edited = plan.Tasks.Where(t => t.Action is PlanTask.Edit or PlanTask.Create).Select(t => t.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        tests.Decisions = (tests.Decisions ?? []).Where(d => d is not null).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var decision in tests.Decisions)
        {
            decision.Decision = decision.Decision?.Trim().ToLowerInvariant() ?? PlanTests.Keep;
            decision.Reason = string.IsNullOrWhiteSpace(decision.Reason) ? null : decision.Reason.Trim();
            if (UnitTestText.ParseNumber(decision.Test) is not { } number || !rules.Known.TryGetValue(number, out var testFile))
            {
                errors.Add($"tests: '{decision.Test}' is not an active test of the catalog; use the numbers from \"# Unit tests\".");
                continue;
            }
            decision.Test = number;
            if (!seen.Add(number)) errors.Add($"tests: {number} has more than one decision.");
            if (decision.Decision is not (PlanTests.Keep or PlanTests.Update or PlanTests.Obsolete))
                errors.Add($"tests: the decision for {number} must be \"keep\", \"update\" or \"obsolete\", not \"{decision.Decision}\".");
            else if (decision.Decision != PlanTests.Keep && decision.Reason is null)
                errors.Add($"tests: {decision.Decision} of {number} needs \"reason\": why, quoting the request.");
            if (decision.Decision == PlanTests.Update && !edited.Contains(testFile))
                errors.Add($"tests: {number} is updated, so the plan needs an edit task for its test file '{testFile}'.");
        }

        tests.New = (tests.New ?? []).Where(n => n is not null).ToList();
        if (tests.New.Count > 0 && !rules.AllowNew)
            errors.Add("tests: new tests are turned off for this project; drop \"new\" and say so in the summary.");
        if (tests.New.Count > MaxNewTests)
            errors.Add($"tests: {tests.New.Count} new tests; the maximum is {MaxNewTests}. Keep the cases that guard the request best.");
        foreach (var test in tests.New)
        {
            test.Title = test.Title?.Trim() ?? string.Empty;
            test.Name = test.Name?.Trim() ?? string.Empty;
            test.File = (test.File ?? string.Empty).Trim().Replace('\\', '/').TrimStart('.', '/');
            test.Scenario = test.Scenario?.Trim() ?? string.Empty;
            test.Purpose = string.IsNullOrWhiteSpace(test.Purpose) ? null : test.Purpose.Trim();
            test.Targets = test.Targets?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            test.Requires = test.Requires?.Select(r => r.Trim().ToLowerInvariant()).Where(Requirements.Contains).Distinct().ToList();

            var label = test.Name.Length > 0 ? $"new test \"{test.Name}\"" : "a new test";
            if (test.Name.Length == 0) errors.Add("tests: every new test needs \"name\", the method name or it()/test() title as it will appear in the code.");
            if (test.Title.Length == 0) errors.Add($"tests: {label} needs \"title\".");
            if (test.Scenario.Length == 0) errors.Add($"tests: {label} needs \"scenario\" (given / when / then with concrete values).");
            if (test.File.Length == 0) errors.Add($"tests: {label} needs \"file\".");
            else if (!edited.Contains(test.File))
                errors.Add($"tests: {label} lives in '{test.File}', but no create or edit task targets that file. Add the task that writes the test.");
        }

        if (tests.Decisions.All(d => d.Decision == PlanTests.Keep) && tests.New.Count == 0) plan.Tests = null;
    }

    // A plan without tasks may only ask for tests to run.
    private static void ValidateTestRun(ThreadPlan plan, UnitTestRules? rules, List<string> errors)
    {
        if (rules is null || plan.Tests is not { } tests)
        {
            plan.Tests = null;
            return;
        }
        tests.Decisions = [];
        // New tests only exist once a task writes them; dropping them silently would leave the request untested.
        if (rules.AllowNew && (tests.New ?? []).Any(n => n is not null))
            errors.Add("tests.new needs tasks that write the tests: add a create or edit task for each test file (and for the test project when there is none). A plan without tasks can only run registered tests (tests.run).");
        tests.New = [];
        var run = new List<string>();
        foreach (var item in (tests.Run ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()))
        {
            if (string.Equals(item, PlanTests.All, StringComparison.OrdinalIgnoreCase))
            {
                run = [PlanTests.All];
                break;
            }
            if (UnitTestText.ParseNumber(item) is { } number && rules.Known.ContainsKey(number)) run.Add(number);
            else errors.Add($"tests.run: '{item}' is not an active test of the catalog; give numbers from \"# Unit tests\" or [\"all\"].");
        }
        tests.Run = run.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tests.Run.Count == 0) plan.Tests = null;
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

    // Several extracts may fill the same new file (the first creates it, the next ones add to it); a create task may not.
    private static void ValidateExtract(PlanTask task, string label, WorkspaceScope scope, Dictionary<string, string> arrivals, Dictionary<string, PlanTask> byId, List<string> errors)
    {
        if (task.Members is not { Count: > 0 })
            errors.Add($"{label}: action \"extract\" needs \"members\", the names of the members to move.");
        if (!IsCSharp(task.File))
            errors.Add($"{label}: \"extract\" only works on C# files (.cs); use edit tasks for other languages.");
        if (task.To is null)
        {
            errors.Add($"{label}: action \"extract\" needs \"to\", the .cs file that receives the members.");
            return;
        }
        if (Resolve(task.To, label, scope, errors) is not { } fullPath) return;

        task.To = scope.ToRelative(fullPath);
        if (!IsCSharp(task.To))
            errors.Add($"{label}: \"to\" must be a .cs file.");
        else if (string.Equals(task.To, task.File, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{label}: \"to\" must be a different file than \"file\".");
        else if (!File.Exists(fullPath) && !arrivals.TryAdd(task.To, task.Id) && !(byId.TryGetValue(arrivals[task.To], out var other) && other.IsExtract))
            errors.Add($"{label}: task {arrivals[task.To]} already creates '{task.To}'. An extract to a new file creates it by itself; drop the create task.");
    }

    private static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static void ValidateTest(ThreadPlan plan, bool requireTest, List<string> errors)
    {
        if (!requireTest)
        {
            plan.Test = null;
            return;
        }
        if (plan.Test is not { } test)
        {
            errors.Add("\"test\" is required: say how the tester can check the change on the running app (level \"ui\" or \"api\" with baseUrl and scenarios), or level \"none\" with the reason in \"notes\".");
            return;
        }

        test.Level = test.Level?.Trim().ToLowerInvariant() ?? string.Empty;
        test.BaseUrl = string.IsNullOrWhiteSpace(test.BaseUrl) ? null : test.BaseUrl.Trim().TrimEnd('/');
        test.Notes = string.IsNullOrWhiteSpace(test.Notes) ? null : test.Notes.Trim();
        test.Scenarios = (test.Scenarios ?? []).Where(s => s is not null).ToList();
        foreach (var scenario in test.Scenarios)
        {
            scenario.Title = scenario.Title?.Trim() ?? string.Empty;
            scenario.Target = string.IsNullOrWhiteSpace(scenario.Target) ? null : scenario.Target.Trim();
            scenario.Steps = (scenario.Steps ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            scenario.Expect = scenario.Expect?.Trim() ?? string.Empty;
        }

        if (test.Level is not (TestPlan.Ui or TestPlan.Api or TestPlan.None))
        {
            errors.Add($"test: level must be \"ui\", \"api\" or \"none\", not \"{test.Level}\".");
            return;
        }
        if (!test.IsRunnable)
        {
            test.Scenarios = [];
            if (test.Notes is null) errors.Add("test: level \"none\" needs \"notes\" saying why the change cannot be tested from outside.");
            return;
        }

        if (test.BaseUrl is null)
            errors.Add($"test: level \"{test.Level}\" needs \"baseUrl\", the root URL the app runs at on this machine (see launchSettings.json, appsettings Kestrel/urls, package.json scripts, the dev server config or .env).");
        else if (!TestUrls.IsLocal(test.BaseUrl))
            errors.Add($"test: baseUrl must be an absolute http(s) URL on this machine (localhost, 127.0.0.1 or [::1]), not '{test.BaseUrl}'. The tester never calls other hosts.");

        if (test.Scenarios.Count == 0)
            errors.Add("test: add at least one scenario (title, steps, expect).");
        else if (test.Scenarios.Count > MaxTestScenarios)
            errors.Add($"test: {test.Scenarios.Count} scenarios; the maximum is {MaxTestScenarios}. Keep the main path and the most important edge cases.");
        for (var i = 0; i < test.Scenarios.Count; i++)
        {
            var scenario = test.Scenarios[i];
            if (scenario.Title.Length == 0) scenario.Title = $"Scenario {i + 1}";
            if (scenario.Expect.Length == 0) errors.Add($"test: scenario \"{scenario.Title}\" needs \"expect\", what the tester must see when it works.");
        }
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

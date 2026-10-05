using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class PlanValidatorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly WorkspaceScope _scope;

    public PlanValidatorTests()
    {
        _workspace.Write("src/Web/Views/User/Index.cshtml", "<form></form>");
        _workspace.Write("src/Web/bin/Debug/app.dll", "binary");
        _scope = new WorkspaceScope(_workspace.Root);
    }

    public void Dispose() => _workspace.Dispose();

    private static PlanTask Task(string id, string file, string action = "edit", params string[] dependsOn) =>
        new() { Id = id, Title = id, File = file, Action = action, Instruction = "do it", DependsOn = [.. dependsOn] };

    [Fact]
    public void Valid_plan_passes_and_paths_are_normalized()
    {
        var plan = new ThreadPlan
        {
            Summary = "ok",
            Tasks = [Task("T1", @".\src\Web\Views\User\Index.cshtml"), Task("T2", "database/new.sql", "create", "T1")]
        };

        Assert.Empty(PlanValidator.Validate(plan, _scope));
        Assert.Equal("src/Web/Views/User/Index.cshtml", plan.Tasks[0].File);
    }

    [Fact]
    public void Answer_only_plan_is_valid()
    {
        Assert.Empty(PlanValidator.Validate(new ThreadPlan { Summary = "q", Answer = "the answer" }, _scope));
    }

    [Theory]
    [InlineData("src/Missing.cs", "edit", "does not exist")]
    [InlineData("src/Web/Views/User/Index.cshtml", "create", "already exists")]
    [InlineData("../outside.txt", "edit", "outside the project root")]
    [InlineData("src/Web/bin/Debug/app.dll", "edit", "generated or third-party")]
    [InlineData("src/Web/Views/User/Index.cshtml", "rename", "action must be")]
    [InlineData("src/Missing.cs", "delete", "cannot be deleted")]
    [InlineData("src/Web/bin/Debug/app.dll", "delete", "generated or third-party")]
    public void Rejects_bad_targets(string file, string action, string expected)
    {
        var plan = new ThreadPlan { Summary = "x", Tasks = [Task("T1", file, action)] };

        Assert.Contains(PlanValidator.Validate(plan, _scope), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Absolute_path_outside_the_root_is_refused_for_changes_even_though_it_can_be_read()
    {
        var outside = Path.Combine(Path.GetTempPath(), "aithread-outside.txt");
        var plan = new ThreadPlan { Summary = "x", Tasks = [Task("T1", outside, "create")] };

        Assert.Contains(PlanValidator.Validate(plan, _scope), e => e.Contains("inside the project root"));
        Assert.True(_scope.TryResolveForRead(outside, out _, out _));
    }

    [Fact]
    public void Rejects_unknown_dependency_and_cycles()
    {
        var unknown = new ThreadPlan { Summary = "x", Tasks = [Task("T1", "a.sql", "create", "T9")] };
        Assert.Contains(PlanValidator.Validate(unknown, _scope), e => e.Contains("unknown task"));

        var cycle = new ThreadPlan
        {
            Summary = "x",
            Tasks = [Task("T1", "a.sql", "create", "T2"), Task("T2", "b.sql", "create", "T1")]
        };
        Assert.Contains(PlanValidator.Validate(cycle, _scope), e => e.Contains("cycle"));
    }

    [Fact]
    public void Rejects_two_tasks_creating_the_same_file()
    {
        var plan = new ThreadPlan { Summary = "x", Tasks = [Task("T1", "a.sql", "create"), Task("T2", "a.sql", "create")] };

        Assert.Contains(PlanValidator.Validate(plan, _scope), e => e.Contains("already creates"));
    }

    private const string View = "src/Web/Views/User/Index.cshtml";

    private static PlanTask MoveTask(string id, string file, string? to) =>
        new() { Id = id, Title = id, File = file, Action = "move", To = to };

    [Fact]
    public void Move_and_delete_need_no_instruction_and_paths_are_normalized()
    {
        _workspace.Write("src/Old.cs", "class Old { }");
        var plan = new ThreadPlan
        {
            Summary = "x",
            Tasks =
            [
                MoveTask("T1", View, @"src\Web\Views\Account\Index.cshtml"),
                new PlanTask { Id = "T2", Title = "T2", File = "src/Old.cs", Action = "DELETE" }
            ]
        };

        Assert.Empty(PlanValidator.Validate(plan, _scope));
        Assert.Equal("src/Web/Views/Account/Index.cshtml", plan.Tasks[0].To);
        Assert.Equal($"Move {View} to src/Web/Views/Account/Index.cshtml.", plan.Tasks[0].Instruction);
        Assert.Equal("delete", plan.Tasks[1].Action);
        Assert.Equal("Delete src/Old.cs.", plan.Tasks[1].Instruction);
    }

    [Theory]
    [InlineData(null, "needs \"to\"")]
    [InlineData(View, "different path")]
    [InlineData("SRC/web/views/user/index.cshtml", "different path")]
    [InlineData("src/Web/Views/User/Index.cshtml/../Index.cshtml", "different path")]
    [InlineData("../outside.cshtml", "outside the project root")]
    [InlineData("src/Web/bin/Index.cshtml", "generated or third-party")]
    public void Rejects_bad_move_destinations(string? to, string expected)
    {
        var plan = new ThreadPlan { Summary = "x", Tasks = [MoveTask("T1", View, to)] };

        Assert.Contains(PlanValidator.Validate(plan, _scope), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_moving_onto_an_existing_file_or_onto_another_tasks_target()
    {
        _workspace.Write("src/Other.cs", "x");
        var existing = new ThreadPlan { Summary = "x", Tasks = [MoveTask("T1", View, "src/Other.cs")] };
        Assert.Contains(PlanValidator.Validate(existing, _scope), e => e.Contains("already exists"));

        var clash = new ThreadPlan { Summary = "x", Tasks = [Task("T1", "src/New.cs", "create"), MoveTask("T2", View, "src/New.cs")] };
        Assert.Contains(PlanValidator.Validate(clash, _scope), e => e.Contains("already creates or moves"));
    }

    [Fact]
    public void A_moved_or_deleted_file_cannot_be_touched_by_another_task()
    {
        var editSource = new ThreadPlan { Summary = "x", Tasks = [MoveTask("T1", View, "src/New.cshtml"), Task("T2", View)] };
        Assert.Contains(PlanValidator.Validate(editSource, _scope), e => e.Contains("Target 'src/New.cshtml' instead"));

        var editDeleted = new ThreadPlan { Summary = "x", Tasks = [Task("T1", View, "delete"), Task("T2", View)] };
        Assert.Contains(PlanValidator.Validate(editDeleted, _scope), e => e.Contains("is deleted by task T1"));

        var twice = new ThreadPlan { Summary = "x", Tasks = [Task("T1", View, "delete"), MoveTask("T2", View, "src/New.cshtml")] };
        Assert.Contains(PlanValidator.Validate(twice, _scope), e => e.Contains("already deletes or moves"));
    }

    [Fact]
    public void Editing_a_moved_file_at_its_new_path_requires_depending_on_the_move()
    {
        var missing = new ThreadPlan { Summary = "x", Tasks = [MoveTask("T1", View, "src/New.cshtml"), Task("T2", "src/New.cshtml")] };
        Assert.Contains(PlanValidator.Validate(missing, _scope), e => e.Contains("Add \"T1\" to dependsOn"));

        var ordered = new ThreadPlan { Summary = "x", Tasks = [MoveTask("T1", View, "src/New.cshtml"), Task("T2", "src/New.cshtml", "edit", "T1")] };
        Assert.Empty(PlanValidator.Validate(ordered, _scope));
    }
}

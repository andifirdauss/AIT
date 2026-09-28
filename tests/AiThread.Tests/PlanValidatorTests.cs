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
    [InlineData("src/Web/Views/User/Index.cshtml", "delete", "action must be")]
    public void Rejects_bad_targets(string file, string action, string expected)
    {
        var plan = new ThreadPlan { Summary = "x", Tasks = [Task("T1", file, action)] };

        Assert.Contains(PlanValidator.Validate(plan, _scope), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
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
}

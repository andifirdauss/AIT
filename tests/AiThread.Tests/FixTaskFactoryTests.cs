using AiThread.Service.Orchestration;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class FixTaskFactoryTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly WorkspaceScope _scope;

    public FixTaskFactoryTests()
    {
        _workspace.Write("src/UserService.cs", string.Join('\n', Enumerable.Range(1, 30).Select(i => i == 20 ? "        var user = repo.Find(id);" : $"// line {i}")));
        _workspace.Write("src/UserController.cs", "class UserController {}");
        _workspace.Write("src/Legacy.cs", "class Legacy {}");
        _workspace.Write("src/obj/Generated.cs", "// generated");
        _workspace.Write("wwwroot/js/user.js", "$('#txtDesc').val();");
        _scope = new WorkspaceScope(_workspace.Root);
    }

    public void Dispose() => _workspace.Dispose();

    private string Full(string relative) => Path.Combine(_workspace.Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private BuildDiagnostic Error(string relative, int line, string code = "CS0103", string message = "The name 'repo' does not exist") =>
        new("error", Full(relative), line, 9, code, message);

    private static BuildResult Failed(params BuildDiagnostic[] errors) => new("dotnet build App.slnx", false, false, 1, errors, 0, string.Empty, 1000);

    private static readonly PlanTask Original = new()
    {
        Id = "T1",
        Title = "Add lookup",
        File = "src/UserService.cs",
        Instruction = "Add the user lookup.",
        Context = [new ContextRef { File = "src/UserController.cs", StartLine = 1, EndLine = 5 }]
    };

    [Fact]
    public void Build_errors_in_changed_files_become_one_task_per_file()
    {
        var build = Failed(Error("src/UserService.cs", 20), Error("src/UserService.cs", 21, "CS1002", "; expected"));

        var fix = FixTaskFactory.Create(1, build, null, ["src/UserService.cs"], [Original], _scope);

        var task = Assert.Single(fix.Tasks);
        Assert.Equal("V1-1", task.Id);
        Assert.Equal("src/UserService.cs", task.File);
        Assert.Equal("edit", task.Action);
        Assert.Equal("Fix build errors", task.Title);
        Assert.Contains("line 20, column 9: CS0103: The name 'repo' does not exist", task.Instruction);
        Assert.Contains("; expected", task.Instruction);
        Assert.Contains("Add the user lookup.", task.Instruction);
        Assert.Equal([1, 20, 21], task.Focus!.Select(f => f.StartLine));
        Assert.Equal("src/UserController.cs", Assert.Single(task.Context!).File);
        Assert.Empty(fix.Unfixable);
    }

    [Fact]
    public void Errors_the_thread_cannot_own_are_reported_not_fixed()
    {
        var build = Failed(
            Error("src/Legacy.cs", 1),
            Error("src/obj/Generated.cs", 1),
            new BuildDiagnostic("error", null, null, null, "NU1101", "Unable to find package Foo."),
            new BuildDiagnostic("error", @"C:\Program Files\dotnet\sdk\Sdk.targets", 90, 5, "NETSDK1045", "The current .NET SDK does not support targeting .NET 99."));

        var fix = FixTaskFactory.Create(1, build, null, ["src/UserService.cs", "src/obj/Generated.cs"], [Original], _scope);

        Assert.Empty(fix.Tasks);
        Assert.Equal(4, fix.Unfixable.Count);
        Assert.Contains(fix.Unfixable, u => u.Contains("did not change"));
        Assert.Contains(fix.Unfixable, u => u.Contains("generated or third-party"));
        Assert.Equal(2, fix.Unfixable.Count(u => u.Contains("not in a project file")));
    }

    [Fact]
    public void Locked_output_and_passing_builds_produce_nothing()
    {
        var locked = Failed(new BuildDiagnostic("error", null, null, null, "MSB3027", "Could not copy App.dll; the file is locked."));

        Assert.Empty(FixTaskFactory.Create(1, locked, null, ["src/UserService.cs"], [Original], _scope).Tasks);
        Assert.Empty(FixTaskFactory.Create(1, Failed() with { Succeeded = true, ExitCode = 0 }, null, [], [], _scope).Unfixable);
    }

    [Fact]
    public void Failed_build_without_recognised_errors_is_reported()
    {
        var fix = FixTaskFactory.Create(2, Failed(), null, ["src/UserService.cs"], [Original], _scope);

        Assert.Empty(fix.Tasks);
        Assert.Contains("without a recognisable error", Assert.Single(fix.Unfixable));
    }

    [Fact]
    public void Other_uses_of_a_name_from_the_error_are_shown_as_the_pattern_to_follow()
    {
        const string pattern = "using (var db = new OracleEntities(EFClientUtility.GetConnectionString()))";
        const string other = "using (var context = new OracleEntities(EFClientUtility.GetConnectionString()))";
        _workspace.Write("src/KlaimBL.cs", string.Join('\n', Enumerable.Range(1, 600).Select(i => i switch
        {
            // The same line twice teaches nothing new, so only its first use is shown.
            120 or 250 => "            " + pattern,
            380 => "            " + other,
            465 => "            using (var db = new OracleEntities())",
            _ => $"// line {i}"
        })));
        var build = Failed(Error("src/KlaimBL.cs", 465, "CS7036",
            "There is no argument given that corresponds to the required parameter 'connectionString' of 'OracleEntities.OracleEntities(string)'"));
        var original = new PlanTask { Id = "T1", Title = "Add fee summary", File = "src/KlaimBL.cs", Instruction = "Add GetFeeSummaryByMKPP." };

        var task = Assert.Single(FixTaskFactory.Create(1, build, null, ["src/KlaimBL.cs"], [original], _scope).Tasks);

        Assert.Equal([1, 120, 380, 465], task.Focus!.Select(f => f.StartLine));
        Assert.Contains($"- line 120: {pattern}", task.Instruction);
        Assert.Contains($"- line 380: {other}", task.Instruction);
        Assert.DoesNotContain("- line 250:", task.Instruction);
    }

    [Fact]
    public void Missing_name_is_fixed_in_the_file_the_plan_meant_to_declare_it()
    {
        _workspace.Write("src/CarryForward.cs", "partial class KiosService\n{\n    void CopySettings() { }\n}");
        _workspace.Write("src/MapEmployee.cs", "partial class KiosService\n{\n    void Map() { CarryForwardNonKiosApps(employee, previous); }\n}");
        PlanTask[] plan =
        [
            new() { Id = "T1", Title = "Add helper", File = "src/CarryForward.cs", Instruction = "Add private method CarryForwardNonKiosApps(employee, previous) that copies non-KIOS apps." },
            new() { Id = "T2", Title = "Call helper", File = "src/MapEmployee.cs", Instruction = "Call CarryForwardNonKiosApps(employee, previous) in Map." }
        ];
        var build = Failed(Error("src/MapEmployee.cs", 3, "CS0103", "The name 'CarryForwardNonKiosApps' does not exist in the current context"));

        var fix = FixTaskFactory.Create(1, build, null, ["src/CarryForward.cs", "src/MapEmployee.cs"], plan, _scope);

        var task = Assert.Single(fix.Tasks);
        Assert.Equal("src/CarryForward.cs", task.File);
        Assert.Equal("Fix build errors", task.Title);
        Assert.Contains("- CarryForwardNonKiosApps — used in src/MapEmployee.cs line 3: CS0103", task.Instruction);
        Assert.Contains("Add private method CarryForwardNonKiosApps", task.Instruction);
        Assert.Equal([1, 4], task.Focus!.Select(f => f.StartLine));
        var callSite = Assert.Single(task.Context!);
        Assert.Equal("src/MapEmployee.cs", callSite.File);
        Assert.Empty(fix.Unfixable);
    }

    [Fact]
    public void Missing_name_stays_with_the_calling_file_when_the_declaring_file_already_has_it()
    {
        _workspace.Write("src/CarryForward.cs", "static class KiosHelpers\n{\n    public static void CarryForwardNonKiosApps(int a) { }\n}");
        _workspace.Write("src/MapEmployee.cs", "partial class KiosService\n{\n    void Map() { CarryForwardNonKiosApps(employee); }\n}");
        PlanTask[] plan =
        [
            new() { Id = "T1", Title = "Add helper", File = "src/CarryForward.cs", Instruction = "Add CarryForwardNonKiosApps." },
            new() { Id = "T2", Title = "Call helper", File = "src/MapEmployee.cs", Instruction = "Call CarryForwardNonKiosApps in Map." }
        ];
        var build = Failed(Error("src/MapEmployee.cs", 3, "CS0103", "The name 'CarryForwardNonKiosApps' does not exist in the current context"));

        var task = Assert.Single(FixTaskFactory.Create(1, build, null, ["src/CarryForward.cs", "src/MapEmployee.cs"], plan, _scope).Tasks);

        Assert.Equal("src/MapEmployee.cs", task.File);
    }

    [Fact]
    public void Review_issues_may_target_any_project_file_and_focus_on_their_anchor()
    {
        var review = new ReviewResult(false, "Selector mismatch.",
        [
            new ReviewIssue("wwwroot/js/user.js", "$('#txtDesc').val();", "Reads #txtDesc but the view uses txtDescription.", "Use $('#txtDescription')."),
            new ReviewIssue("src/UserService.cs", "var user = repo.Find(id);", "repo is not injected.", "Use _repository."),
            new ReviewIssue("../outside.cs", null, "x", "y"),
            new ReviewIssue("src/Missing.cs", null, "x", "y")
        ], 100);
        var build = Failed(Error("src/UserService.cs", 20));

        var fix = FixTaskFactory.Create(2, build, review, ["src/UserService.cs"], [Original], _scope);

        Assert.Equal(["V2-1", "V2-2"], fix.Tasks.Select(t => t.Id));
        var service = fix.Tasks.Single(t => t.File == "src/UserService.cs");
        Assert.Equal("Fix build errors and review findings", service.Title);
        Assert.Contains("Use _repository.", service.Instruction);

        var script = fix.Tasks.Single(t => t.File == "wwwroot/js/user.js");
        Assert.Equal("Fix review findings", script.Title);
        Assert.Equal([1], script.Focus!.Select(f => f.StartLine));
        Assert.Contains("Near: $('#txtDesc').val();", script.Instruction);

        Assert.Contains(fix.Unfixable, u => u.Contains("outside the project root"));
        Assert.Contains(fix.Unfixable, u => u.Contains("does not exist"));
    }
}

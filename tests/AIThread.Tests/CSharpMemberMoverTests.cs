using AIThread.Service.Orchestration;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AIThread.Tests;

public class CSharpMemberMoverTests : IDisposable
{
    private const string OrderService = """
        using System;
        using System.Collections.Generic;

        namespace Shop.Services
        {
            /// <summary>Orders.</summary>
            [Serializable]
            public class OrderService : IOrderService
            {
                private readonly List<int> _ids = new();
                private int _a, _b;

                public OrderService() { }

                #region Queries

                /// <summary>Gets one.</summary>
                public int Get(int id)
                {
                    return Find(id) + _a + _b;
                }

                public int Get(string code) => code.Length;

                private int Find(int id) => _ids.Contains(id) ? id : -1;

                #endregion

                public void Save(int id)
                {
                    _ids.Add(id);
                }
            }

            public interface IOrderService
            {
                int Get(int id);
                void Save(int id);
            }
        }

        """;

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    // Both parts must compile together without errors (duplicates, unbalanced #region, missing usings, ...).
    private static void AssertCompiles(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Test", sources.Select(s => CSharpSyntaxTree.ParseText(s)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Moves_members_with_their_overloads_and_doc_comments_into_a_new_partial_file()
    {
        var result = CSharpMemberMover.Move(Lf(OrderService), null, ["Get", "Find()"]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["Get", "Get", "Find"], result.Moved);
        Assert.Contains("public partial class OrderService : IOrderService", result.Source);
        Assert.DoesNotContain("Get(", result.Source.Split("public interface")[0]);
        Assert.Contains("#region Queries", result.Source);
        Assert.Contains("#endregion", result.Source);

        Assert.Equal(Lf("""
            using System;
            using System.Collections.Generic;

            namespace Shop.Services
            {
                public partial class OrderService
                {
                    /// <summary>Gets one.</summary>
                    public int Get(int id)
                    {
                        return Find(id) + _a + _b;
                    }

                    public int Get(string code) => code.Length;

                    private int Find(int id) => _ids.Contains(id) ? id : -1;
                }
            }

            """), result.Target);
        Assert.Equal(["public int Get(int id)", "public int Get(string code)", "private int Find(int id)"], result.Signatures);
        AssertCompiles(result.Source, result.Target!);
    }

    [Fact]
    public void Nested_types_and_file_scoped_namespaces_become_partial_on_every_level()
    {
        const string source = """
            namespace App;

            public static class Outer
            {
                internal sealed class Inner<T> where T : class
                {
                    public int X() => 1;

                    public int Y() => 2;
                }
            }

            """;

        var result = CSharpMemberMover.Move(Lf(source), null, ["X"]);

        Assert.True(result.Success, result.Error);
        Assert.Contains("public static partial class Outer", result.Source);
        Assert.Contains("internal sealed partial class Inner<T> where T : class", result.Source);
        Assert.Equal(Lf("""
            namespace App;

            public static partial class Outer
            {
                internal sealed partial class Inner<T>
                {
                    public int X() => 1;
                }
            }

            """), result.Target);
        AssertCompiles(result.Source, result.Target!);
    }

    [Fact]
    public void Adds_to_an_existing_part_and_only_removes_members_it_already_declares()
    {
        const string part = """
            namespace Shop.Services
            {
                public partial class OrderService
                {
                    public int Get(int id)
                    {
                        return Find(id) + _a + _b;
                    }
                }
            }

            """;

        var result = CSharpMemberMover.Move(Lf(OrderService), Lf(part), ["Get", "Find", "Save"]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["Get"], result.Deduplicated);
        Assert.Equal(["Get", "Find", "Save"], result.Moved);
        Assert.Single(result.Target!.Split("public int Get(int id)")[1..]);
        Assert.Contains("public void Save(int id)", result.Target);
        AssertCompiles(result.Source, result.Target!);
    }

    [Fact]
    public void Refuses_unknown_members_and_half_of_a_field_declaration()
    {
        Assert.Contains("Missing", CSharpMemberMover.Move(Lf(OrderService), null, ["Get", "Missing"]).Error);
        Assert.Contains("_a, _b", CSharpMemberMover.Move(Lf(OrderService), null, ["_a"]).Error);
        Assert.True(CSharpMemberMover.Move(Lf(OrderService), null, ["_a", "_b"]).Success);
    }

    [Fact]
    public void Refuses_a_target_without_the_type()
    {
        var result = CSharpMemberMover.Move(Lf(OrderService), "namespace Shop.Services { class Other { } }", ["Save"]);

        Assert.Contains("Shop.Services.OrderService", result.Error);
    }

    [Fact]
    public async Task Extract_task_writes_both_files_keeping_line_endings_and_bom()
    {
        var source = _workspace.Write("src/OrderService.cs", OrderService.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        await File.WriteAllBytesAsync(source, [0xEF, 0xBB, 0xBF, .. await File.ReadAllBytesAsync(source)]);
        var scope = new WorkspaceScope(_workspace.Root);
        var task = new PlanTask { Id = "T1", File = "src/OrderService.cs", Action = PlanTask.Extract, To = "src/OrderService.Save.cs", Members = ["Save"] };

        var outcome = await new FileOperations(new FileLockProvider()).RunAsync(task, scope, null, CancellationToken.None);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Contains("Save", outcome.Summary);
        Assert.Contains("+public void Save(int id)", outcome.Diff);
        var target = await File.ReadAllBytesAsync(Path.Combine(_workspace.Root, "src", "OrderService.Save.cs"));
        Assert.Equal([0xEF, 0xBB, 0xBF], target[..3]);
        var targetText = System.Text.Encoding.UTF8.GetString(target[3..]);
        Assert.Contains("public void Save(int id)\r\n", targetText);
        Assert.DoesNotContain("public void Save", await File.ReadAllTextAsync(source));
        Assert.DoesNotContain("\n", (await File.ReadAllTextAsync(source)).Replace("\r\n", string.Empty));
    }

    [Fact]
    public void Validator_accepts_extracts_into_one_new_file_and_names_the_members()
    {
        _workspace.Write("src/OrderService.cs", OrderService);
        var plan = new ThreadPlan
        {
            Summary = "split",
            Tasks =
            [
                new PlanTask { Id = "T1", File = "src/OrderService.cs", Action = "extract", To = @"src\OrderService.Queries.cs", Members = ["Get(int id)", "Find"] },
                new PlanTask { Id = "T2", File = "src/OrderService.cs", Action = "extract", To = "src/OrderService.Queries.cs", Members = ["OrderService.Save"] }
            ]
        };

        Assert.Empty(PlanValidator.Validate(plan, new WorkspaceScope(_workspace.Root)));
        Assert.Equal(["Get", "Find"], plan.Tasks[0].Members);
        Assert.Equal("Move Get, Find from src/OrderService.cs to src/OrderService.Queries.cs.", plan.Tasks[0].Instruction);
        Assert.Equal(["Save"], plan.Tasks[1].Members);
    }

    [Fact]
    public void Validator_rejects_bad_extracts()
    {
        _workspace.Write("src/OrderService.cs", OrderService);
        _workspace.Write("src/site.js", "x();");
        var plan = new ThreadPlan
        {
            Summary = "split",
            Tasks =
            [
                new PlanTask { Id = "T1", File = "src/OrderService.cs", Action = "extract", To = "src/A.cs" },
                new PlanTask { Id = "T2", File = "src/site.js", Action = "extract", To = "src/B.cs", Members = ["x"] },
                new PlanTask { Id = "T3", File = "src/C.cs", Action = "create", Instruction = "new" },
                new PlanTask { Id = "T4", File = "src/OrderService.cs", Action = "extract", To = "src/C.cs", Members = ["Save"] },
                new PlanTask { Id = "T5", File = "src/A.cs", Action = "edit", Instruction = "tweak" }
            ]
        };

        var errors = PlanValidator.Validate(plan, new WorkspaceScope(_workspace.Root));

        Assert.Contains(errors, e => e.StartsWith("Task T1") && e.Contains("\"members\""));
        Assert.Contains(errors, e => e.StartsWith("Task T2") && e.Contains("only works on C# files"));
        Assert.Contains(errors, e => e.StartsWith("Task T4") && e.Contains("drop the create task"));
        Assert.Contains(errors, e => e.StartsWith("Task T5") && e.Contains("extracts members into it") && e.Contains("\"T1\""));
    }

    [Fact]
    public void Duplicate_member_errors_finish_the_move_instead_of_deleting_the_new_copy()
    {
        _workspace.Write("src/OrderService.cs", OrderService);
        _workspace.Write("src/OrderService.Save.cs", "namespace Shop.Services { public partial class OrderService { public void Save(int id) { } } }");
        var scope = new WorkspaceScope(_workspace.Root);
        var full = Path.Combine(_workspace.Root, "src", "OrderService.Save.cs");
        var build = new BuildResult("dotnet build", false, false, 1,
        [
            new BuildDiagnostic("error", full, 1, 70, "CS0111", "Type 'OrderService' already defines a member called 'Save' with the same parameter types"),
            new BuildDiagnostic("error", full, 1, 80, "CS0103", "The name 'repo' does not exist in the current context")
        ], 0, string.Empty, 1000);
        var review = new ReviewResult(false, "not split", [new ReviewIssue("src/OrderService.cs", null, "Save is still in the main file", "Remove Save")], 0);
        PlanTask[] earlier =
        [
            new() { Id = "T1", File = "src/OrderService.Save.cs", Action = "create", Instruction = "Put Save here." },
            new() { Id = "T2", File = "src/OrderService.cs", Action = "edit", Instruction = "Remove the moved methods." }
        ];

        var fix = FixTaskFactory.Create(1, build, review, ["src/OrderService.cs", "src/OrderService.Save.cs"], earlier, scope);

        Assert.Equal(2, fix.Tasks.Count);
        var extract = fix.Tasks[0];
        Assert.Equal(("V1-1", "extract", "src/OrderService.cs", "src/OrderService.Save.cs"), (extract.Id, extract.Action, extract.File, extract.To));
        Assert.Equal(["Save"], extract.Members);

        var edit = fix.Tasks[1];
        Assert.Equal("src/OrderService.Save.cs", edit.File);
        Assert.Equal(["V1-1"], edit.DependsOn);
        Assert.Contains("CS0103", edit.Instruction);
        Assert.DoesNotContain("CS0111", edit.Instruction);
        Assert.Empty(fix.Unfixable);
    }
}

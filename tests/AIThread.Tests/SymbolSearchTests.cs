using System.Linq;
using AIThread.Service.Workspace;
using AIThread.Service.Orchestration;
using Xunit;

namespace AIThread.Tests;

public class SymbolSearchTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private SymbolSearch Search() => new(new WorkspaceScope(_workspace.Root));

    [Fact]
    public void Csharp_class_and_method_declarations_are_found()
    {
        _workspace.Write("src/UserService.cs", "namespace App;\npublic class UserService\n{\n    public void Save(int id) { }\n    public string Name { get; set; } = \"\";\n}\n");

        var classResult = Search().FindSymbol("UserService");
        Assert.Contains("src/UserService.cs:2: class", classResult);

        var methodResult = Search().FindSymbol("Save");
        Assert.Contains("src/UserService.cs:4: method", methodResult);
        Assert.Contains("(in UserService)", methodResult);
        Assert.DoesNotContain("{ }", methodResult);

        var propertyResult = Search().FindSymbol("Name", kind: "property");
        Assert.Contains("src/UserService.cs:5: property", propertyResult);
    }

    [Fact]
    public void Qualified_name_restricts_to_the_container()
    {
        _workspace.Write("src/Two.cs", "class A\n{\n    public void Run() { }\n}\nclass B\n{\n    public void Run() { }\n}\n");

        var result = Search().FindSymbol("B.Run");

        Assert.StartsWith("1 declaration(s):", result);
        Assert.Contains("(in B)", result);
    }

    [Fact]
    public void Kind_type_filter_excludes_members()
    {
        _workspace.Write("src/Order.cs", "class Order { }\nclass Factory\n{\n    void Order() { }\n}\n");

        var result = Search().FindSymbol("Order", kind: "type");

        Assert.Contains(": class", result);
        Assert.DoesNotContain(": method", result);
    }

    [Fact]
    public void Csharp_references_skip_comments_and_strings()
    {
        _workspace.Write("src/Use.cs", "class Use\n{\n    // Save is mentioned here\n    string s = \"Save\";\n    void Save() { }\n    void Call() { Save(); }\n}\n");

        var result = Search().FindReferences("Save");

        Assert.StartsWith("2 reference(s) in 1 file(s):", result);
        Assert.Contains("src/Use.cs:5:", result);
        Assert.Contains("[declaration]", result);
        Assert.Contains("src/Use.cs:6:", result);
        Assert.DoesNotContain("src/Use.cs:3:", result);
        Assert.DoesNotContain("src/Use.cs:4:", result);
    }

    [Fact]
    public void Typescript_function_and_class_are_found()
    {
        _workspace.Write("web/app.ts", "export class Cart {\n  addItem(id: string) {\n  }\n}\nexport function total(items: number[]) {\n  return 0;\n}\nconst x = total([]);\n");

        Assert.Contains("web/app.ts:1: class", Search().FindSymbol("Cart"));
        Assert.Contains("web/app.ts:5: function", Search().FindSymbol("total"));

        var methodResult = Search().FindSymbol("addItem");
        Assert.Contains("web/app.ts:2: method", methodResult);
        Assert.Contains("(in Cart)", methodResult);

        var refs = Search().FindReferences("total");
        Assert.Contains("web/app.ts:8:", refs);
        Assert.Contains("[declaration]", refs);
    }

    [Fact]
    public void Python_method_gets_its_class_as_container()
    {
        _workspace.Write("py/models.py", "class User:\n    def save(self):\n        pass\n\ndef load():\n    pass\n");

        var saveResult = Search().FindSymbol("save");
        Assert.Contains("py/models.py:2: method", saveResult);
        Assert.Contains("(in User)", saveResult);

        Assert.Contains("py/models.py:5: function", Search().FindSymbol("load"));
    }

    [Fact]
    public void Java_class_and_method_are_found()
    {
        _workspace.Write("java/Repo.java", "public class Repo {\n    public List<String> findAll(int max) {\n        return null;\n    }\n}\n");

        Assert.Contains("java/Repo.java:1: class", Search().FindSymbol("Repo"));
        Assert.Contains("java/Repo.java:2: method", Search().FindSymbol("findAll"));
    }

    [Fact]
    public void Unknown_name_returns_a_clear_message()
    {
        Assert.Equal("No declarations of 'Missing'.", Search().FindSymbol("Missing"));
        Assert.Equal("No references to 'Missing'.", Search().FindReferences("Missing"));
        Assert.StartsWith("Error:", Search().FindSymbol(""));
    }

    [Fact]
    public void Symbol_tools_are_offered_to_the_planner()
    {
        var names = PlannerTools.All()
            .Select(t => t!["function"]!["name"]!.GetValue<string>())
            .ToList();

        Assert.Contains(PlannerTools.FindSymbol, names);
        Assert.Contains(PlannerTools.FindReferences, names);
        Assert.Equal(PlannerTools.SubmitPlan, names[^1]);
    }
}

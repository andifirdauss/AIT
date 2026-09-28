using System.Text;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class WorkspaceTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Finds_solution_in_directory_parent_and_single_child()
    {
        var sln = _workspace.Write("repo/src/App.slnx", "<Solution><Project Path=\"Web/Web.csproj\" /></Solution>");
        _workspace.Write("repo/src/Web/Controllers/HomeController.cs", "class C {}");

        Assert.Equal(sln, SolutionLocator.Find(Path.Combine(_workspace.Root, "repo", "src")));
        Assert.Equal(sln, SolutionLocator.Find(Path.Combine(_workspace.Root, "repo", "src", "Web", "Controllers")));
        Assert.Equal(sln, SolutionLocator.Find(Path.Combine(_workspace.Root, "repo")));
    }

    [Fact]
    public void Several_child_solutions_are_ambiguous()
    {
        _workspace.Write("a/A.sln", "");
        _workspace.Write("b/B.sln", "");

        Assert.Null(SolutionLocator.Find(_workspace.Root));
    }

    [Fact]
    public void Reads_projects_from_sln_and_slnx()
    {
        var slnx = _workspace.Write("x/X.slnx", "<Solution><Folder Name=\"/src/\"><Project Path=\"src/A/A.csproj\" /></Folder></Solution>");
        var sln = _workspace.Write("y/Y.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"src\\B\\B.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");

        Assert.Equal(["src/A/A.csproj"], SolutionLocator.ReadProjects(slnx));
        Assert.Equal(["src/B/B.csproj"], SolutionLocator.ReadProjects(sln));
    }

    [Fact]
    public void Scope_blocks_escapes_and_hides_build_output()
    {
        var scope = new WorkspaceScope(_workspace.Root);

        Assert.True(scope.TryResolve("src/a.cs", out _, out _));
        Assert.False(scope.TryResolve("../../etc/passwd", out _, out var error));
        Assert.Contains("outside", error);
        Assert.True(WorkspaceScope.IsIgnoredRelativePath("src/Web/obj/x.cs"));
        Assert.True(WorkspaceScope.IsIgnoredRelativePath("src/Web/wwwroot/lib/jquery/jquery.js"));
        Assert.False(WorkspaceScope.IsIgnoredRelativePath("src/Web/wwwroot/js/site.js"));
    }

    [Fact]
    public async Task Text_io_preserves_bom_and_line_endings()
    {
        var path = Path.Combine(_workspace.Root, "Index.cshtml");
        await File.WriteAllTextAsync(path, "line1\r\nline2\r\n", new UTF8Encoding(true));

        var state = await TextFileIo.ReadAsync(path);
        Assert.Equal("line1\nline2\n", state.Content);
        Assert.True(state.HasBom);
        Assert.Equal("\r\n", state.NewLine);

        await TextFileIo.WriteAsync(path, state.Content + "line3\n", state);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal("line1\r\nline2\r\nline3\r\n", new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public async Task Tools_search_and_read_skip_ignored_folders()
    {
        _workspace.Write("src/UserService.cs", "public class UserService\n{\n    void Validate() {}\n}\n");
        _workspace.Write("src/bin/UserService.cs", "public class UserService {}");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var search = tools.SearchText("class UserService", null, isRegex: false);
        Assert.Contains("src/UserService.cs:1", search);
        Assert.DoesNotContain("src/bin", search);

        Assert.Equal("src/UserService.cs", tools.FindFiles("*Service.cs"));

        var read = await tools.ReadFileAsync("src/UserService.cs", 2, 3);
        Assert.Contains("(lines 2-3 of 4)", read);
        Assert.Contains("3|     void Validate() {}", read);
    }
}

using System.Text;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class WorkspaceTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private string At(params string[] parts) => System.IO.Path.Combine([_workspace.Root, .. parts]);

    // The resolver walks up from the start folder; the tests stop it at the workspace so files outside it
    // (a stray package.json in the temp folder) cannot change the outcome.
    [Fact]
    public void Repository_root_is_found_from_a_sub_folder()
    {
        _workspace.Write("repo/.git/HEAD", "ref: refs/heads/main");
        _workspace.Write("repo/src/App.slnx", "<Solution />");
        _workspace.Write("repo/src/Web/Controllers/HomeController.cs", "class C {}");

        var root = ProjectLocator.Resolve(At("repo", "src", "Web", "Controllers"), maxParentLevels: 3).Root;

        Assert.NotNull(root);
        Assert.Equal(At("repo"), root.Path);
        Assert.Equal("repo", root.Name);
        Assert.True(root.IsRepository);
    }

    [Fact]
    public void Nearest_repository_wins_over_manifests_inside_it()
    {
        _workspace.Write("repo/.git/HEAD", "");
        _workspace.Write("repo/services/api/pom.xml", "<project />");

        Assert.Equal(At("repo"), ProjectLocator.Resolve(At("repo", "services", "api"), maxParentLevels: 2).Root?.Path);
    }

    [Fact]
    public void Without_a_repository_the_outermost_solution_wins()
    {
        _workspace.Write("app/App.sln", "");
        _workspace.Write("app/src/Web/Web.csproj", "<Project />");

        var root = ProjectLocator.Resolve(At("app", "src", "Web"), maxParentLevels: 2).Root;

        Assert.Equal(At("app"), root?.Path);
        Assert.Equal([".NET"], root?.Stacks);
    }

    [Fact]
    public void A_parent_pom_with_modules_wins_over_its_module()
    {
        _workspace.Write("svc/pom.xml", "<project><modules><module>core</module></modules></project>");
        _workspace.Write("svc/core/pom.xml", "<project />");
        _workspace.Write("svc/core/src/Main.java", "class Main {}");

        Assert.Equal(At("svc"), ProjectLocator.Resolve(At("svc", "core", "src"), maxParentLevels: 2).Root?.Path);
    }

    [Fact]
    public void Nearest_manifest_wins_when_nothing_ties_the_modules_together()
    {
        _workspace.Write("tools/pom.xml", "<project />");
        _workspace.Write("tools/cli/pom.xml", "<project />");
        _workspace.Write("tools/cli/src/Main.java", "class Main {}");

        Assert.Equal(At("tools", "cli"), ProjectLocator.Resolve(At("tools", "cli", "src"), maxParentLevels: 2).Root?.Path);
    }

    [Fact]
    public void Folder_without_any_marker_is_not_a_project()
    {
        _workspace.Write("proklamasi/notes.txt", "teks proklamasi");

        var resolution = ProjectLocator.Resolve(At("proklamasi"), maxParentLevels: 1);

        Assert.Null(resolution.Root);
        Assert.Empty(resolution.Nearby);
    }

    [Fact]
    public void Folder_holding_several_projects_is_ambiguous_and_lists_them()
    {
        _workspace.Write("a/A.sln", "");
        _workspace.Write("b/pom.xml", "<project />");
        _workspace.Write("c/package.json", "{}");
        _workspace.Write("d/readme.txt", "");

        var resolution = ProjectLocator.Resolve(_workspace.Root, maxParentLevels: 0);

        Assert.Null(resolution.Root);
        Assert.Equal(["a", "b", "c"], resolution.Nearby.Select(p => p.Name));
    }

    [Theory]
    [InlineData("pom.xml", "Java (Maven)")]
    [InlineData("build.gradle.kts", "JVM (Gradle)")]
    [InlineData("package.json", "Node.js")]
    [InlineData("pyproject.toml", "Python")]
    [InlineData("go.mod", "Go")]
    [InlineData("Cargo.toml", "Rust")]
    [InlineData("composer.json", "PHP")]
    [InlineData("App.csproj", ".NET")]
    [InlineData("App.slnx", ".NET")]
    public void Manifests_identify_the_stack(string file, string stack)
    {
        _workspace.Write($"p/{file}", "");

        var root = ProjectLocator.Inspect(At("p"));

        Assert.NotNull(root);
        Assert.Equal([stack], root.Stacks);
        Assert.False(root.IsRepository);
    }

    [Fact]
    public void Repository_without_a_manifest_is_still_a_project()
    {
        _workspace.Write("notes/.git/HEAD", "");

        var root = ProjectLocator.Inspect(At("notes"));

        Assert.NotNull(root);
        Assert.True(root.IsRepository);
        Assert.Empty(root.Stacks);
    }

    [Fact]
    public void Ordinary_files_are_not_markers()
    {
        _workspace.Write("p/readme.md", "");
        _workspace.Write("p/src/Program.cs", "");

        Assert.Null(ProjectLocator.Inspect(At("p")));
    }

    [Fact]
    public void Drives_home_and_workspaces_are_judged_as_project_roots_correctly()
    {
        Assert.True(ProjectLocator.IsBlockedRoot(System.IO.Path.GetPathRoot(_workspace.Root)!));
        Assert.False(ProjectLocator.IsBlockedRoot(_workspace.Root));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0) Assert.True(ProjectLocator.IsBlockedRoot(home));
    }

    [Fact]
    public void Manifests_are_listed_relative_to_the_root_and_skip_ignored_folders()
    {
        _workspace.Write("repo/src/Web/Web.csproj", "");
        _workspace.Write("repo/services/api/pom.xml", "");
        _workspace.Write("repo/web/package.json", "");
        _workspace.Write("repo/node_modules/x/package.json", "");
        _workspace.Write("repo/bin/x/App.csproj", "");

        var manifests = ProjectLocator.FindManifests(At("repo"));

        Assert.Equal(
            ["services/api/pom.xml", "src/Web/Web.csproj", "web/package.json"],
            manifests.Select(m => m.RelativePath).Order(StringComparer.Ordinal));
        Assert.Equal([".NET", "Java (Maven)", "Node.js"], ProjectLocator.StacksOf(manifests));
    }

    [Fact]
    public void Scan_finds_project_roots_within_depth_and_does_not_enter_projects()
    {
        _workspace.Write("a/A.sln", "");
        _workspace.Write("a/inner/pom.xml", "");
        _workspace.Write("b/src/.git/HEAD", "");
        _workspace.Write("c/1/2/3/package.json", "");
        _workspace.Write("d/node_modules/pkg/package.json", "");
        _workspace.Write(".hidden/package.json", "");

        var result = ProjectLocator.Scan(_workspace.Root, maxDepth: 2, maxResults: 100, maxFolders: 1000, CancellationToken.None);

        Assert.False(result.Truncated);
        Assert.Equal([At("a"), At("b", "src")], result.Projects.Select(p => p.Path));
    }

    [Fact]
    public void Scan_reports_truncation_when_a_limit_is_hit()
    {
        _workspace.Write("a/A.sln", "");
        _workspace.Write("b/B.sln", "");
        _workspace.Write("c/C.sln", "");

        var result = ProjectLocator.Scan(_workspace.Root, maxDepth: 3, maxResults: 2, maxFolders: 1000, CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(2, result.Projects.Count);
    }

    [Fact]
    public void Browsable_directories_hide_build_and_dot_folders()
    {
        _workspace.Write("src/a.cs", "");
        _workspace.Write("bin/x.dll", "");
        _workspace.Write(".git/HEAD", "");

        var names = ProjectLocator.BrowsableDirectories(new DirectoryInfo(_workspace.Root)).Select(d => d.Name);

        Assert.Equal(["src"], names);
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
        Assert.True(WorkspaceScope.IsIgnoredRelativePath("api/__pycache__/x.pyc"));
        Assert.True(WorkspaceScope.IsIgnoredRelativePath("app/.gradle/caches/x"));
    }

    [Fact]
    public void Stack_specific_folders_are_hidden_only_for_those_stacks()
    {
        var plain = new WorkspaceScope(_workspace.Root);
        var maven = new WorkspaceScope(_workspace.Root, ProjectLocator.IgnoredDirectoriesFor(["Java (Maven)"]));

        Assert.False(plain.IsIgnoredPath("target/classes/A.class"));
        Assert.False(maven.IsIgnoredPath("build/scripts/build.ps1"));
        Assert.True(maven.IsIgnoredPath("target/classes/A.class"));
        Assert.True(maven.IsIgnoredPath("api/target/classes/A.class"));
        Assert.False(maven.IsIgnoredPath("api/src/main/java/A.java"));
    }

    [Fact]
    public async Task Text_io_preserves_bom_and_line_endings()
    {
        var path = System.IO.Path.Combine(_workspace.Root, "Index.cshtml");
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

    [Fact]
    public async Task Read_tools_can_look_outside_the_project_root()
    {
        _workspace.Write("proj/a.txt", "mine");
        _workspace.Write("other/Notes.md", "hello other\n");
        _workspace.Write("other/src/Util.cs", "class Util {}");
        _workspace.Write("other/node_modules/x/Skip.cs", "class Skip {}");
        var other = At("other").Replace('\\', '/');
        var tools = new WorkspaceTools(new WorkspaceScope(At("proj")));

        Assert.Contains("Notes.md", tools.ListDirectory(other));
        Assert.StartsWith(other + "/", tools.ListDirectory(other));

        var read = await tools.ReadFileAsync($"{other}/Notes.md", null, null);
        Assert.Contains($"File: {other}/Notes.md", read);
        Assert.Contains("1| hello other", read);
        Assert.Contains("hello other", await tools.ReadFileAsync("../other/Notes.md", null, null));

        Assert.Equal($"{other}/src/Util.cs", tools.FindFiles("**/*.cs", other));
        Assert.Contains($"{other}/Notes.md:1", tools.SearchText("hello", null, isRegex: false, path: other));

        // Without a path the search stays in the project.
        Assert.Equal("No files match '*.md'.", tools.FindFiles("*.md"));
        Assert.Contains("Error:", tools.ListDirectory(other + "/missing"));
    }

    [Fact]
    public void Changes_still_have_to_stay_inside_the_project_root()
    {
        _workspace.Write("proj/a.txt", "mine");
        _workspace.Write("other/Notes.md", "x");
        var scope = new WorkspaceScope(At("proj"));

        Assert.True(scope.TryResolve("a.txt", out _, out _));
        Assert.False(scope.TryResolve(At("other", "Notes.md"), out _, out var error));
        Assert.Contains("outside the project root", error);
        Assert.False(scope.TryResolve("../other/Notes.md", out _, out _));

        Assert.True(scope.TryResolveForRead(At("other", "Notes.md"), out var read, out _));
        Assert.Equal(At("other", "Notes.md"), read);
        Assert.Equal("a.txt", scope.ToDisplay(At("proj", "a.txt")));
        Assert.Equal(At("other", "Notes.md").Replace('\\', '/'), scope.ToDisplay(read));
    }

    [Fact]
    public void Tools_skip_build_output_of_the_detected_stack()
    {
        _workspace.Write("src/A.java", "class A {}");
        _workspace.Write("target/A.java", "class A {}");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root, ProjectLocator.IgnoredDirectoriesFor(["Java (Maven)"])));

        Assert.Equal("src/A.java", tools.FindFiles("*.java"));
    }
}

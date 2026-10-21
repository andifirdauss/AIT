using System.Diagnostics;
using System.Text.Json.Nodes;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;

namespace AIThread.Tests;

// Planner tools git_status / git_diff / git_log / git_show / git_blame (GitTools) on a real repository.
public class GitToolsTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        // git writes its objects read-only, which Directory.Delete refuses on Windows.
        foreach (var file in Directory.EnumerateFiles(_workspace.Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        _workspace.Dispose();
    }

    private GitTools Git(string? root = null) => new(new WorkspaceScope(root ?? _workspace.Root));

    [Fact]
    public async Task Status_and_diff_show_what_is_not_committed_yet()
    {
        Init();
        _workspace.Write("src/Greeter.cs", "class Greeter\n{\n    string Hello() => \"Hi\";\n}\n");
        _workspace.Write("src/Other.cs", "class Other { }\n");
        Commit("Add greeter");
        _workspace.Write("src/Greeter.cs", "class Greeter\n{\n    string Hello() => \"Halo\";\n}\n");
        _workspace.Write("src/Other.cs", "class Other { int X; }\n");
        RunGit("add", "src/Other.cs");
        _workspace.Write("src/New.cs", "class New { }\n");

        var status = await Git().StatusAsync(null);
        Assert.StartsWith("Branch: main", status);
        Assert.Contains(" M src/Greeter.cs", status);
        Assert.Contains("M  src/Other.cs", status);
        Assert.Contains("?? src/New.cs", status);
        Assert.Contains("3 changed file(s)", status);

        var diff = await Git().DiffAsync(null, null, staged: false, stat: false);
        Assert.StartsWith("Working tree against HEAD (staged and not staged):", diff);
        Assert.Contains("-    string Hello() => \"Hi\";", diff);
        Assert.Contains("+    string Hello() => \"Halo\";", diff);
        Assert.Contains("+class Other { int X; }", diff);
        Assert.DoesNotContain("New.cs", diff);

        var staged = await Git().DiffAsync(null, null, staged: true, stat: false);
        Assert.Contains("src/Other.cs", staged);
        Assert.DoesNotContain("Greeter", staged);

        var stat = await Git().DiffAsync("src/Greeter.cs", null, staged: false, stat: true);
        Assert.Contains("src/Greeter.cs | 2 +-", stat);
        Assert.DoesNotContain("Other.cs", stat);
    }

    [Fact]
    public async Task Log_show_and_blame_follow_the_history()
    {
        Init();
        _workspace.Write("src/Greeter.cs", "class Greeter\n{\n}\n");
        Commit("Add greeter");
        _workspace.Write("src/Greeter.cs", "class Greeter\n{\n    string Hello() => \"Hi\";\n}\n");
        Commit("Add Hello to greeter");
        _workspace.Write("src/Greeter.cs", "class Greeter\n{\n    string Hello() => \"Hi\";\n    string Bye() => \"Bye\";\n}\n");

        var log = await Git().LogAsync(null, null, null, null, null, files: false);
        var newer = log.IndexOf("AIT Test: Add Hello to greeter", StringComparison.Ordinal);
        var older = log.IndexOf("AIT Test: Add greeter", StringComparison.Ordinal);
        Assert.True(newer >= 0 && older > newer, log);

        var introduced = await Git().LogAsync("src/Greeter.cs", null, null, null, "Hello()", files: true);
        Assert.Contains("Add Hello to greeter", introduced);
        Assert.DoesNotContain(": Add greeter", introduced);
        Assert.Contains("M\tsrc/Greeter.cs", introduced);

        Assert.Equal("No commits match.", await Git().LogAsync(null, null, null, "nothing like this", null, files: false));

        var show = await Git().ShowAsync("HEAD", null, stat: false);
        Assert.Contains("Add Hello to greeter", show);
        Assert.Contains("+    string Hello() => \"Hi\";", show);
        var shownStat = await Git().ShowAsync("HEAD~1", null, stat: true);
        Assert.Contains("src/Greeter.cs", shownStat);
        Assert.DoesNotContain("@@", shownStat);

        var blame = await Git().BlameAsync("src/Greeter.cs", 3, 4);
        Assert.StartsWith("Blame: src/Greeter.cs (lines 3-4 of 5;", blame);
        Assert.Contains("AIT Test", blame);
        Assert.Contains("Not Committed Yet", blame);
        Assert.Contains("startLine=5", blame);
    }

    [Theory]
    [InlineData("--output=hacked.txt")]
    [InlineData("-p")]
    [InlineData("main; rm -rf .")]
    [InlineData("HEAD with space")]
    public async Task Revisions_can_never_become_options(string revision)
    {
        Init();
        _workspace.Write("a.txt", "a\n");
        Commit("init");

        Assert.StartsWith("Error: ", await Git().DiffAsync(null, revision, staged: false, stat: false));
        Assert.StartsWith("Error: ", await Git().LogAsync(null, revision, null, null, null, files: false));
        Assert.StartsWith("Error: ", await Git().ShowAsync(revision, null, stat: false));
        Assert.False(File.Exists(Path.Combine(_workspace.Root, "hacked.txt")));
    }

    [Fact]
    public async Task A_project_below_the_repository_root_sees_its_own_paths()
    {
        Init();
        _workspace.Write("app/src/A.cs", "class A { }\n");
        _workspace.Write("tools/B.cs", "class B { }\n");
        Commit("init");
        _workspace.Write("app/src/A.cs", "class A { int X; }\n");
        _workspace.Write("tools/B.cs", "class B { int Y; }\n");
        var project = Path.Combine(_workspace.Root, "app");

        Assert.True(GitTools.IsRepository(project));
        var status = await Git(project).StatusAsync(null);
        Assert.Contains(" M src/A.cs", status);
        Assert.DoesNotContain("B.cs", status);
        var diff = await Git(project).DiffAsync(null, null, staged: false, stat: false);
        Assert.Contains("a/src/A.cs", diff);
        Assert.DoesNotContain("B.cs", diff);
    }

    [Fact]
    public async Task A_repository_without_commits_explains_there_is_nothing_to_compare_with()
    {
        Init();
        _workspace.Write("a.txt", "a\n");

        Assert.Contains("?? a.txt", await Git().StatusAsync(null));
        Assert.Contains("no commits yet", await Git().DiffAsync(null, null, staged: false, stat: false));
        Assert.Equal("The repository has no commits yet.", await Git().LogAsync(null, null, null, null, null, files: false));
    }

    [Fact]
    public void Git_tools_are_offered_only_inside_a_repository()
    {
        Assert.False(GitTools.IsRepository(_workspace.Root));
        _workspace.Write(".git/HEAD", "ref: refs/heads/main\n");
        Assert.True(GitTools.IsRepository(Path.Combine(_workspace.Root, "src")));

        var without = PlannerTools.All(canBuild: true).Select(Name).ToList();
        Assert.DoesNotContain(without, PlannerTools.Git.Contains);
        var with = PlannerTools.All(canBuild: true, canGit: true).Select(Name).ToList();
        Assert.All(PlannerTools.Git, tool => Assert.Contains(tool, with));

        Assert.Contains(PlannerTools.GitDiff, PlannerTools.Delegable);
        Assert.False(PlannerTools.Offered(PlannerTools.GitDiff, canBuild: true, canGit: false));
        Assert.True(PlannerTools.Offered(PlannerTools.ReadFile, canBuild: false, canGit: false));
        Assert.False(PlannerTools.Offered(PlannerTools.RunBuild, canBuild: false, canGit: true));
    }

    [Fact]
    public void Git_tool_lines_describe_their_arguments()
    {
        Assert.Equal("`main...HEAD` staged `src/A.cs` stat", PlannerToolRunner.Describe(PlannerTools.GitDiff,
            new JsonObject { ["base"] = "main...HEAD", ["staged"] = true, ["path"] = "src/A.cs", ["stat"] = true }));
        Assert.Equal("`src/A.cs` -S \"Hello()\" max 5", PlannerToolRunner.Describe(PlannerTools.GitLog,
            new JsonObject { ["path"] = "src/A.cs", ["change"] = "Hello()", ["limit"] = 5 }));
        Assert.Equal("`src/A.cs` (10-20)", PlannerToolRunner.Describe(PlannerTools.GitBlame,
            new JsonObject { ["path"] = "src/A.cs", ["startLine"] = 10, ["endLine"] = 20 }));
    }

    private void Init() => RunGit("init", "--initial-branch=main", "--quiet");

    private void Commit(string message)
    {
        RunGit("add", "--all");
        RunGit("commit", "--quiet", "-m", message);
    }

    // Fixed identity and no signing or line ending conversion, whatever the machine's git config says.
    private void RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _workspace.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string[] config = ["-c", "user.name=AIT Test", "-c", "user.email=ait@test.local", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];
        foreach (var argument in config.Concat(arguments)) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
    }

    private static string Name(JsonNode? tool) => tool!["function"]!["name"]!.GetValue<string>();
}

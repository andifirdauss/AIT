using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Git;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// publish.branch and publish.pullRequest: a new branch before the commit, a pull request after the push (gh/glab, or
// the link to open it).
public class GitBranchPullRequestTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly TempWorkspace _remote = new();

    public void Dispose()
    {
        _workspace.Dispose();
        _remote.Dispose();
    }

    [Theory]
    [InlineData("feature/order-export", true)]
    [InlineData("fix-123", true)]
    [InlineData("release/1.2", true)]
    [InlineData("has space", false)]
    [InlineData("-leading", false)]
    [InlineData("a..b", false)]
    [InlineData("trailing/", false)]
    [InlineData("ends.", false)]
    [InlineData("x.lock", false)]
    [InlineData("a/.hidden", false)]
    [InlineData("what?", false)]
    [InlineData("a~1", false)]
    [InlineData("a@{b", false)]
    [InlineData("@", false)]
    public void Branch_names_follow_the_basic_git_rules(string name, bool valid) =>
        Assert.Equal(valid, GitPublisher.IsValidBranchName(name));

    [Fact]
    public void Validator_checks_the_branch_and_that_a_pull_request_is_pushed()
    {
        var scope = new WorkspaceScope(_workspace.Root);
        var badBranch = new ThreadPlan { Summary = "x", Answer = "ok", Publish = new PlanPublish { Commit = true, Message = "m", Branch = "bad name" } };
        Assert.Contains(PlanValidator.Validate(badBranch, scope), e => e.Contains("\"branch\""));

        var notPushed = new ThreadPlan { Summary = "x", Answer = "ok", Publish = new PlanPublish { Commit = true, Message = "m", PullRequest = new PlanPullRequest() } };
        Assert.Contains(PlanValidator.Validate(notPushed, scope), e => e.Contains("needs \"push\""));

        var titled = new ThreadPlan
        {
            Summary = "x", Answer = "ok",
            Publish = new PlanPublish { Commit = true, Push = true, Message = "Add export\n\nbody", Branch = " feature/export ", PullRequest = new PlanPullRequest { Body = " " } }
        };
        Assert.Empty(PlanValidator.Validate(titled, scope));
        Assert.Equal("feature/export", titled.Publish!.Branch);
        Assert.Equal("Add export", titled.Publish.PullRequest!.Title);
        Assert.Null(titled.Publish.PullRequest.Body);

        var branchOnly = new ThreadPlan { Summary = "x", Answer = "ok", Publish = new PlanPublish { Branch = "feature/x" } };
        Assert.Empty(PlanValidator.Validate(branchOnly, scope));
        Assert.NotNull(branchOnly.Publish);
    }

    [Theory]
    [InlineData("https://github.com/org/shop.git", "https://github.com/org/shop/compare/main...feature/x?expand=1")]
    [InlineData("git@github.com:org/shop.git", "https://github.com/org/shop/compare/main...feature/x?expand=1")]
    [InlineData("ssh://git@github.com/org/shop", "https://github.com/org/shop/compare/main...feature/x?expand=1")]
    [InlineData("https://gitlab.com/group/sub/shop.git", "https://gitlab.com/group/sub/shop/-/merge_requests/new?merge_request[source_branch]=feature%2Fx&merge_request[target_branch]=main")]
    [InlineData("git@gitlab.example.com:group/shop.git", "https://gitlab.example.com/group/shop/-/merge_requests/new?merge_request[source_branch]=feature%2Fx&merge_request[target_branch]=main")]
    [InlineData("https://bitbucket.org/team/shop.git", "https://bitbucket.org/team/shop/pull-requests/new?source=feature%2Fx&dest=main")]
    [InlineData("https://org@dev.azure.com/org/proj/_git/shop", "https://dev.azure.com/org/proj/_git/shop/pullrequestcreate?sourceRef=feature%2Fx&targetRef=main")]
    [InlineData("git@ssh.dev.azure.com:v3/org/proj/shop", "https://dev.azure.com/org/proj/_git/shop/pullrequestcreate?sourceRef=feature%2Fx&targetRef=main")]
    public void Compare_url_opens_the_pull_request_page_of_the_host(string remote, string expected) =>
        Assert.Equal(expected, GitPublisher.CompareUrl(remote, "main", "feature/x"));

    [Theory]
    [InlineData(@"C:\repos\shop.git")]
    [InlineData("/srv/git/shop.git")]
    [InlineData("https://git.example.com/org/shop.git")]
    public void Compare_url_is_null_for_a_local_remote_or_an_unknown_host(string remote) =>
        Assert.Null(GitPublisher.CompareUrl(remote, "main", "feature/x"));

    [Fact]
    public async Task A_new_branch_gets_the_commit_and_the_push_and_a_pull_request_on_an_unknown_host_is_left_to_the_developer()
    {
        using var _ = Lang.Use("en");
        InitRepository();
        var thread = _workspace.Write("src/App.cs", "class App { int X; }");
        var publisher = new GitPublisher(_ => null);

        var (job, error) = await publisher.PrepareAsync([thread],
            new PublishRequest(Commit: true, Push: true, "Add X", Branch: "feature/x", PullRequest: new PublishPullRequest("Add X", "Why", "main")));

        Assert.Null(error);
        Assert.Equal([ApprovalKind.Branch, ApprovalKind.PullRequest, ApprovalKind.Commit, ApprovalKind.Push], job!.Items.Select(i => i.Kind));
        Assert.EndsWith("|feature/x", job.Items[0].Target);
        Assert.EndsWith("|main<-feature/x", job.Items[1].Target);
        Assert.EndsWith("|origin/feature/x", job.Items[3].Target);
        Assert.False(job.PushesProtectedBranch);

        var outcome = await publisher.RunAsync(job);

        Assert.True(outcome.Succeeded, string.Join("\n", outcome.Lines));
        Assert.Equal("feature/x", Git(_workspace.Root, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal("Add X", Git(_workspace.Root, "log", "-1", "--format=%s"));
        Assert.Contains("refs/heads/feature/x", Git(_workspace.Root, "ls-remote", "origin"));
        Assert.Equal("origin/feature/x", Git(_workspace.Root, "rev-parse", "--abbrev-ref", "feature/x@{u}"));
        // The base keeps its commit; the bare remote has no web page, so the pull request line asks to open it by hand.
        Assert.NotEqual(Git(_workspace.Root, "rev-parse", "main"), Git(_workspace.Root, "rev-parse", "HEAD"));
        Assert.Contains(outcome.Lines, l => l.Contains("could not be opened automatically") && l.Contains("feature/x"));
    }

    [Fact]
    public async Task The_pull_request_link_is_given_when_the_cli_is_missing()
    {
        using var _ = Lang.Use("en");
        InitRepository();
        // The remote is a GitHub URL; pushes go to the bare repository through url.<base>.pushInsteadOf (get-url keeps
        // the GitHub URL).
        Git(_workspace.Root, "remote", "set-url", "origin", "https://github.com/org/shop.git");
        Git(_workspace.Root, "config", $"url.{_remote.Root.Replace('\\', '/')}.pushInsteadOf", "https://github.com/org/shop.git");
        var thread = _workspace.Write("src/App.cs", "class App { int Y; }");
        var publisher = new GitPublisher(_ => null);

        var (job, error) = await publisher.PrepareAsync([thread],
            new PublishRequest(Commit: true, Push: true, "Add Y", Branch: "feature/y", PullRequest: new PublishPullRequest("Add Y")));
        Assert.Null(error);
        var outcome = await publisher.RunAsync(job!);

        Assert.True(outcome.Succeeded, string.Join("\n", outcome.Lines));
        Assert.Contains(outcome.Lines, l => l.Contains("https://github.com/org/shop/compare/main...feature/y?expand=1") && l.Contains("gh is not installed"));
    }

    [Fact]
    public async Task An_existing_branch_is_never_reused()
    {
        using var _ = Lang.Use("en");
        InitRepository();
        Git(_workspace.Root, "branch", "feature/taken");
        var thread = _workspace.Write("src/App.cs", "class App { int Z; }");

        var (job, error) = await new GitPublisher(_ => null).PrepareAsync([thread], new PublishRequest(Commit: true, Push: false, "Add Z", Branch: "feature/taken"));

        Assert.Null(job);
        Assert.Contains("already exists", error);
        Assert.Equal("main", Git(_workspace.Root, "rev-parse", "--abbrev-ref", "HEAD"));
    }

    [Fact]
    public async Task A_pull_request_into_its_own_branch_is_refused()
    {
        using var _ = Lang.Use("en");
        InitRepository();
        var thread = _workspace.Write("src/App.cs", "class App { int W; }");

        var (job, error) = await new GitPublisher(_ => null).PrepareAsync([thread], new PublishRequest(Commit: true, Push: true, "Add W", PullRequest: new PublishPullRequest("Add W")));

        Assert.Null(job);
        Assert.Contains("into itself", error);
    }

    [Fact]
    public async Task A_plan_with_a_new_branch_asks_for_it_and_commits_there_once_allowed()
    {
        await using var harness = await CreateAsync();
        var root = harness.Project.Root;
        using var remote = new TempWorkspace();
        Git(remote.Root, "init", "--bare", "-b", "main");
        Git(root, "init", "-b", "main");
        Git(root, "config", "user.name", "AIT Test");
        Git(root, "config", "user.email", "ait@test.local");
        Git(root, "config", "commit.gpgsign", "false");
        Git(root, "add", "-A");
        Git(root, "commit", "-m", "init");
        Git(root, "remote", "add", "origin", remote.Root);
        Git(root, "push", "-u", "origin", "main");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")],
                Publish = new PlanPublish { Commit = true, Push = true, Message = "Ganti sapaan jadi Halo", Branch = "feature/halo" }
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan jadi Halo."));

        var (first, firstObserver) = await harness.RunAsync("Ganti sapaan jadi Halo, buat branch feature/halo lalu push", ThreadSource.ClaudeCode, "session-branch");

        Assert.Equal(ThreadStatus.Completed, first.Status);
        Assert.Contains("feature/halo", firstObserver.Final);
        Assert.Contains("Izinkan selalu", firstObserver.Final);
        Assert.Equal("main", Git(root, "rev-parse", "--abbrev-ref", "HEAD"));

        var (second, secondObserver) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-branch");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Contains("feature/halo", secondObserver.Final);
        Assert.Equal("feature/halo", Git(root, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal("Ganti sapaan jadi Halo", Git(root, "log", "-1", "--format=%s"));
        Assert.Equal("init", Git(root, "log", "-1", "--format=%s", "main"));
        Assert.Equal(Git(root, "rev-parse", "HEAD"), Git(remote.Root, "rev-parse", "feature/halo"));
    }

    private void InitRepository()
    {
        Git(_remote.Root, "init", "--bare", "-b", "main");
        _workspace.Write("src/App.cs", "class App {}");
        Git(_workspace.Root, "init", "-b", "main");
        Git(_workspace.Root, "config", "user.name", "AIT Test");
        Git(_workspace.Root, "config", "user.email", "ait@test.local");
        Git(_workspace.Root, "config", "commit.gpgsign", "false");
        Git(_workspace.Root, "add", "-A");
        Git(_workspace.Root, "commit", "-m", "init");
        Git(_workspace.Root, "remote", "add", "origin", _remote.Root);
        Git(_workspace.Root, "push", "-u", "origin", "main");
    }

    private static string Git(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-c", "core.autocrlf=false" }.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Trim();
    }
}

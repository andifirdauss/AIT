using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Git;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Tests;

public class ApprovalTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly WorkspaceScope _scope;

    public ApprovalTests()
    {
        _workspace.Write("src/App.cs", "class App {}");
        _workspace.Write(".env", "DB_PASSWORD=hunter2");
        _scope = new WorkspaceScope(_workspace.Root);
    }

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData("1", ApprovalDecision.Once)]
    [InlineData("izinkan sekali", ApprovalDecision.Once)]
    [InlineData("Allow once.", ApprovalDecision.Once)]
    [InlineData("2", ApprovalDecision.Always)]
    [InlineData("selalu", ApprovalDecision.Always)]
    [InlineData("always allow", ApprovalDecision.Always)]
    [InlineData("3", ApprovalDecision.Deny)]
    [InlineData("tolak", ApprovalDecision.Deny)]
    [InlineData("deny", ApprovalDecision.Deny)]
    [InlineData("5", ApprovalDecision.AlwaysAll)]
    [InlineData("izinkan semua perintah", ApprovalDecision.AlwaysAll)]
    [InlineData("allow all", ApprovalDecision.AlwaysAll)]
    [InlineData("6", null)]
    [InlineData("sekali lagi coba cara lain", null)]
    public void Replies_are_read_in_both_languages(string reply, ApprovalDecision? expected) =>
        Assert.Equal(expected, ApprovalReply.Parse(reply));

    [Fact]
    public void A_target_too_long_to_save_is_not_offered_always()
    {
        using var _ = Lang.Use("en");
        var command = "grep -rn x " + string.Join(' ', Enumerable.Range(0, 100).Select(i => $"src/File{i}.cs"));
        Assert.True(command.Length > ApprovalTargets.MaxLength);

        var request = new ApprovalRequest([new ApprovalItem(ApprovalKind.Command, command)], "Search.");
        Assert.False(request.AllowAlways);
        Assert.DoesNotContain("2. **Always allow**", request.ToText());
        Assert.True(new ApprovalRequest([new ApprovalItem(ApprovalKind.Command, "git status")], "x").AllowAlways);
        // "Allow all commands" still fits: it saves "*", not the command.
        Assert.True(request.AllowAll);
        Assert.Contains("5. **Allow all commands in this project**", request.ToText());
    }

    [Fact]
    public void The_all_commands_grant_covers_every_command_and_nothing_else()
    {
        Assert.True(ApprovalTargets.Covers(ApprovalKind.Command, ApprovalTargets.AllCommands, "rm -rf build && git push"));
        Assert.True(ApprovalTargets.Covers(ApprovalKind.Command, ApprovalTargets.AllCommands, "grep -rn x src; echo === 2>/dev/null"));
        Assert.False(ApprovalTargets.Covers(ApprovalKind.Host, ApprovalTargets.AllCommands, "example.com:443"));
        Assert.False(new ApprovalRequest([new ApprovalItem(ApprovalKind.Push, "C:/repo|origin/feature")], "x").AllowAll);
    }

    [Fact]
    public void A_folder_grant_covers_what_lies_below_it_and_other_kinds_match_exactly()
    {
        var root = Path.Combine(_workspace.Root, "other");
        Assert.True(ApprovalTargets.Covers(ApprovalKind.WriteOutside, root, Path.Combine(root, "src")));
        Assert.True(ApprovalTargets.Covers(ApprovalKind.WriteOutside, root + Path.DirectorySeparatorChar, root));
        Assert.False(ApprovalTargets.Covers(ApprovalKind.WriteOutside, root, root + "-copy"));
        Assert.True(ApprovalTargets.Covers(ApprovalKind.Command, "npm  run lint", "npm run lint"));
        Assert.False(ApprovalTargets.Covers(ApprovalKind.Command, "npm run lint", "npm run lint --fix"));
        Assert.True(ApprovalTargets.Covers(ApprovalKind.Host, "API.example.com:443", "api.example.com:443"));
    }

    [Fact]
    public void The_request_offers_always_only_when_allowed()
    {
        using var _ = Lang.Use("en");
        var item = new ApprovalItem(ApprovalKind.Push, "repo|origin/main");
        Assert.Contains("Always allow", new ApprovalRequest([item], "why").ToText());
        var protectedPush = new ApprovalRequest([item], "why", AllowAlways: false).ToText();
        Assert.DoesNotContain("Always allow", protectedPush);
        Assert.Contains("3. **Deny**", protectedPush);
    }

    [Theory]
    [InlineData("curl -s http://localhost:5000/api/users/1")]
    [InlineData("curl -sS -H \"Accept: application/json\" http://127.0.0.1:8080/health | jq .")]
    [InlineData("curl -I http://localhost:5000")]
    [InlineData("curl -G -d q=1 localhost:3000/search")]
    [InlineData("git status")]
    [InlineData("git log --oneline -n 5")]
    [InlineData("git branch -a")]
    [InlineData("ls src")]
    [InlineData("ls *.cs")]
    [InlineData("cat src/App.cs")]
    [InlineData("dotnet --info")]
    [InlineData("node -v")]
    [InlineData("grep -n App src/App.cs")]
    public void Read_only_commands_run_without_asking(string command) =>
        Assert.Equal(CommandVerdict.ReadOnly, CommandPolicy.Classify(command, _scope, _workspace.Root).Verdict);

    [Theory]
    [InlineData("curl -X POST http://localhost:5000/api/users")]
    [InlineData("curl -d name=x http://localhost:5000/api/users")]
    [InlineData("curl -o out.json http://localhost:5000/api")]
    [InlineData("git push")]
    [InlineData("git commit -m x")]
    [InlineData("git branch feature-x")]
    [InlineData("git -c core.pager=less log")]
    [InlineData("ls; rm -rf src")]
    [InlineData("cat src/App.cs > copy.cs")]
    [InlineData("echo $HOME")]
    [InlineData("npm install")]
    [InlineData("grep -r password .")]
    [InlineData("find . -delete")]
    [InlineData("rm src/App.cs")]
    public void Commands_that_may_change_something_need_the_command_allowed(string command)
    {
        var check = CommandPolicy.Classify(command, _scope, _workspace.Root);

        Assert.Equal(CommandVerdict.NeedsApproval, check.Verdict);
        Assert.Equal(ApprovalKind.Command, Assert.Single(check.Items!).Kind);
    }

    [Fact]
    public void A_curl_to_another_host_needs_that_host_allowed()
    {
        var check = CommandPolicy.Classify("curl -s https://api.example.com/v1/items?page=2 | jq .", _scope, _workspace.Root);

        Assert.Equal(CommandVerdict.NeedsApproval, check.Verdict);
        var item = Assert.Single(check.Items!);
        Assert.Equal(ApprovalKind.Host, item.Kind);
        Assert.Equal("api.example.com:443", item.Target);
    }

    [Theory]
    [InlineData("cat server.key")]
    [InlineData("curl -G --data-urlencode @secrets.json http://localhost:5000")]
    [InlineData("git show HEAD:config/id_rsa")]
    [InlineData("cat ~/.ssh/id_rsa")]
    [InlineData("rm -f deploy.pem")]
    public void Commands_that_read_credential_files_are_refused(string command) =>
        Assert.Equal(CommandVerdict.Refused, CommandPolicy.Classify(command, _scope, _workspace.Root).Verdict);

    [Fact]
    public async Task The_runner_returns_output_and_exit_code()
    {
        var result = await new CommandRunner().RunAsync("git --version", _workspace.Root, TimeSpan.FromSeconds(30));

        Assert.Null(result.StartError);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("git version", result.Output);
    }

    [Fact]
    public async Task Publisher_commits_only_the_thread_files_and_pushes_without_force()
    {
        using var _ = Lang.Use("en");
        using var remote = new TempWorkspace();
        Git(remote.Root, "init", "--bare", "-b", "feature");
        Git(_workspace.Root, "init", "-b", "feature");
        Git(_workspace.Root, "config", "user.name", "AIT Test");
        Git(_workspace.Root, "config", "user.email", "ait@test.local");
        Git(_workspace.Root, "config", "commit.gpgsign", "false");
        Git(_workspace.Root, "add", "src/App.cs");
        Git(_workspace.Root, "commit", "-m", "init");
        Git(_workspace.Root, "remote", "add", "origin", remote.Root);

        var thread = _workspace.Write("src/App.cs", "class App { int X; }");
        _workspace.Write("src/Other.cs", "class Other {}");
        var (job, error) = await new GitPublisher().PrepareAsync([thread], commit: true, push: true, "Add X");

        Assert.Null(error);
        Assert.False(job!.PushesProtectedBranch);
        Assert.Equal([ApprovalKind.Commit, ApprovalKind.Push], job.Items.Select(i => i.Kind));
        var outcome = await new GitPublisher().RunAsync(job);

        Assert.True(outcome.Succeeded, string.Join("\n", outcome.Lines));
        Assert.Equal("Add X", Git(_workspace.Root, "log", "-1", "--format=%s"));
        Assert.Contains("src/Other.cs", Git(_workspace.Root, "status", "--porcelain"));
        Assert.Equal(Git(_workspace.Root, "rev-parse", "HEAD"), Git(remote.Root, "rev-parse", "feature"));
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("release/1.2", true)]
    [InlineData("feature/patharea", false)]
    public void Pushes_to_main_and_release_branches_are_protected(string branch, bool expected) =>
        Assert.Equal(expected, GitPublisher.IsProtectedBranch(branch));

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

using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Git;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Tests;

// GitChangeTracker on a real repository: a thread's change followed through git (staged, committed, squash merged,
// discarded, changed again, left on another branch) by the blob id of what the thread wrote.
public class GitChangeTrackerTests
{
    private const string File1 = "src/Greeter.cs";
    private const string Original = "class Greeter { string Hello() => \"Hi\"; }\n";
    private const string Changed = "class Greeter { string Hello() => \"Halo\"; }\n";

    [Fact]
    public async Task Origin_names_the_branch_commit_and_author()
    {
        await using var harness = await CreateAsync();
        var head = Git(harness, "rev-parse", "HEAD");

        var origin = await harness.Service<IGitChangeTracker>().ReadOriginAsync(harness.Project.Root);

        Assert.Equal(new GitOrigin("main", head, "AIT Test <ait@test.local>"), origin);
        Assert.Null(await harness.Service<IGitChangeTracker>().ReadOriginAsync(Path.GetTempPath()));
    }

    [Fact]
    public async Task A_change_is_followed_from_the_working_tree_to_a_commit()
    {
        await using var harness = await CreateAsync();
        var threadId = await ThreadWritesAsync(harness, File1, Changed);
        var tracker = harness.Service<IGitChangeTracker>();

        Assert.Equal(1, await tracker.RefreshThreadsAsync([threadId]));
        var (thread, file) = await LoadAsync(harness, threadId);
        Assert.Equal(GitChangeState.Uncommitted, thread.GitState);
        Assert.Equal(Git(harness, "hash-object", File1), file.AfterBlob);
        Assert.NotNull(thread.GitCheckedAt);

        Git(harness, "add", File1);
        await tracker.RefreshThreadsAsync([threadId]);
        Assert.Equal(GitChangeState.Staged, (await LoadAsync(harness, threadId)).Thread.GitState);

        Git(harness, "commit", "--quiet", "-m", "Halo");
        var commit = Git(harness, "rev-parse", "HEAD");
        await tracker.RefreshThreadsAsync([threadId]);
        (thread, file) = await LoadAsync(harness, threadId);
        Assert.Equal(GitChangeState.Committed, thread.GitState);
        Assert.Equal(commit, thread.GitCommits);
        Assert.Equal(commit, file.GitCommit);

        // Nothing moved: no state change is reported.
        Assert.Equal(0, await tracker.RefreshThreadsAsync([threadId]));
    }

    [Fact]
    public async Task A_squash_merge_is_recognised_by_its_content_and_a_checkout_leaves_it_on_the_other_branch()
    {
        await using var harness = await CreateAsync();
        Git(harness, "checkout", "--quiet", "-b", "feature");
        var threadId = await ThreadWritesAsync(harness, File1, Changed);
        harness.Project.Write("src/Other.cs", "class Other { }\n");
        Git(harness, "add", "--all");
        Git(harness, "commit", "--quiet", "-m", "Halo on feature");
        var tracker = harness.Service<IGitChangeTracker>();

        Git(harness, "checkout", "--quiet", "main");
        await tracker.RefreshThreadsAsync([threadId]);
        var (thread, _) = await LoadAsync(harness, threadId);
        Assert.Equal(GitChangeState.OnOtherBranch, thread.GitState);
        Assert.Equal(Git(harness, "rev-parse", "feature"), thread.GitCommits);

        Git(harness, "merge", "--quiet", "--squash", "feature");
        Git(harness, "commit", "--quiet", "-m", "Squash feature");
        await tracker.RefreshThreadsAsync([threadId]);
        (thread, _) = await LoadAsync(harness, threadId);
        Assert.Equal(GitChangeState.Committed, thread.GitState);
        Assert.Equal(Git(harness, "rev-parse", "HEAD"), thread.GitCommits);
    }

    [Fact]
    public async Task A_discard_or_an_edit_outside_AIT_is_seen_and_the_session_brief_skips_the_discarded_change()
    {
        await using var harness = await CreateAsync();
        var discarded = await ThreadWritesAsync(harness, File1, Changed);
        var tracker = harness.Service<IGitChangeTracker>();
        await tracker.RefreshThreadsAsync([discarded]);

        Git(harness, "checkout", "--", File1);
        await tracker.RefreshThreadsAsync([discarded]);
        Assert.Equal(GitChangeState.Discarded, (await LoadAsync(harness, discarded)).Thread.GitState);

        var edited = await ThreadWritesAsync(harness, File1, Changed);
        await tracker.RefreshThreadsAsync([edited]);
        harness.Project.Write(File1, "class Greeter { string Hello() => \"Hai\"; }\n");
        await tracker.RefreshThreadsAsync([edited]);
        Assert.Equal(GitChangeState.ModifiedOutside, (await LoadAsync(harness, edited)).Thread.GitState);
    }

    [Fact]
    public async Task A_thread_records_where_it_started_and_queues_its_change_for_the_tracker()
    {
        await using var harness = await CreateAsync();
        harness.Project.Write(ThreadOrchestratorTests.GreeterFile, ThreadOrchestratorTests.Greeter);
        Git(harness, "add", "--all");
        Git(harness, "commit", "--quiet", "-m", "Greeter");
        Git(harness, "checkout", "--quiet", "-b", "feature/halo");
        var head = Git(harness, "rev-parse", "HEAD");
        await harness.AddStandardNodesAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Ganti sapaan.",
                Tasks = [ThreadOrchestratorTests.Step("T1", ThreadOrchestratorTests.GreeterFile, "Return \"Halo\" from Hello().")]
            }))
            .OnExecutor(ThreadOrchestratorTests.GreeterFile, ScriptedRouter.Edit(ThreadOrchestratorTests.HelloLine, ThreadOrchestratorTests.HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, _) = await harness.RunAsync("Ganti sapaan jadi Halo");

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(("feature/halo", head, "AIT Test <ait@test.local>"), (thread.GitBranch, thread.GitBaseCommit, thread.GitAuthor));
        var queued = new List<GitTrackRequest>();
        while (harness.Service<IGitChangeTracker>().Reader.TryRead(out var request)) queued.Add(request);
        Assert.Contains(new GitTrackRequest(harness.ProjectId, result.ThreadId), queued);

        await harness.Service<IGitChangeTracker>().RefreshThreadsAsync([result.ThreadId]);
        Assert.Equal(GitChangeState.Uncommitted, (await harness.LoadThreadAsync(result.ThreadId)).GitState);
    }

    [Fact]
    public void A_thread_takes_the_least_advanced_state_of_the_files_it_still_has()
    {
        Assert.Equal((GitChangeState?)null, GitChangeTracker.Summarize([(null, null)]).State);
        Assert.Equal(GitChangeState.Discarded, GitChangeTracker.Summarize([(GitChangeState.Discarded, null), (GitChangeState.Discarded, null)]).State);
        Assert.Equal(GitChangeState.Staged,
            GitChangeTracker.Summarize([(GitChangeState.Committed, "a"), (GitChangeState.Staged, null), (GitChangeState.Discarded, null)]).State);
        Assert.Equal(GitChangeState.ModifiedOutside, GitChangeTracker.Summarize([(GitChangeState.Committed, "a"), (GitChangeState.ModifiedOutside, null)]).State);
        Assert.Equal(GitChangeState.Committed, GitChangeTracker.Summarize([(GitChangeState.Committed, "a"), (GitChangeState.OnOtherBranch, "b")]).State);
        var other = GitChangeTracker.Summarize([(GitChangeState.OnOtherBranch, "b"), (GitChangeState.Discarded, null)]);
        Assert.Equal(GitChangeState.OnOtherBranch, other.State);
        Assert.Equal(["b"], other.Commits);
    }

    private static async Task<OrchestrationHarness> CreateAsync()
    {
        var harness = await OrchestrationHarness.CreateAsync();
        Git(harness, "init", "--initial-branch=main", "--quiet");
        Git(harness, "config", "user.name", "AIT Test");
        Git(harness, "config", "user.email", "ait@test.local");
        Git(harness, "config", "commit.gpgsign", "false");
        Git(harness, "config", "core.autocrlf", "false");
        harness.Project.Write(File1, Original);
        Git(harness, "add", "--all");
        Git(harness, "commit", "--quiet", "-m", "Initial");
        return harness;
    }

    // A finished thread that wrote one file through the file journal, as the executor does.
    private static async Task<Guid> ThreadWritesAsync(OrchestrationHarness harness, string path, string content)
    {
        var thread = new ThreadRun { ProjectId = harness.ProjectId, Prompt = "Ganti sapaan", Status = ThreadStatus.Completed, CompletedAt = DateTime.UtcNow };
        await using (var db = await harness.CreateDbAsync())
        {
            db.Threads.Add(thread);
            await db.SaveChangesAsync();
        }

        var journal = new FileJournal(harness.Service<IDbContextFactory<AIThread.Data.Context.AIThreadDbContext>>(), thread.Id,
            new WorkspaceScope(harness.Project.Root));
        var fullPath = Path.Combine(harness.Project.Root, path.Replace('/', Path.DirectorySeparatorChar));
        await journal.BeforeWriteAsync(fullPath);
        harness.Project.Write(path, content);
        await journal.AfterWriteAsync(fullPath);
        return thread.Id;
    }

    private static async Task<(ThreadRun Thread, ThreadFileSnapshot File)> LoadAsync(OrchestrationHarness harness, Guid threadId)
    {
        await using var db = await harness.CreateDbAsync();
        var thread = await db.Threads.AsNoTracking().SingleAsync(t => t.Id == threadId);
        var file = await db.ThreadFileSnapshots.AsNoTracking().SingleAsync(s => s.ThreadRunId == threadId);
        return (thread, file);
    }

    private static string Git(OrchestrationHarness harness, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = harness.Project.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Trim();
    }
}

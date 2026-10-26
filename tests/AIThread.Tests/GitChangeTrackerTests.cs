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
    private const string ProjectKey = "github.com/org/demo";

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
    public async Task A_push_a_stash_a_revert_and_a_dropped_commit_are_told_apart()
    {
        await using var harness = await CreateAsync();
        using var remote = new TempWorkspace();
        GitIn(remote.Root, "init", "--bare", "--quiet", "--initial-branch=main");
        Git(harness, "remote", "add", "origin", remote.Root);
        var tracker = harness.Service<IGitChangeTracker>();

        // Stashed, then back in the working tree.
        var stashed = await ThreadWritesAsync(harness, File1, Changed);
        Git(harness, "stash", "--quiet");
        await tracker.RefreshThreadsAsync([stashed]);
        Assert.Equal(GitChangeState.Stashed, (await LoadAsync(harness, stashed)).Thread.GitState);
        Git(harness, "stash", "pop", "--quiet");
        await tracker.RefreshThreadsAsync([stashed]);
        Assert.Equal(GitChangeState.Uncommitted, (await LoadAsync(harness, stashed)).Thread.GitState);

        // Committed, then pushed.
        Git(harness, "commit", "--quiet", "-am", "Halo");
        await tracker.RefreshThreadsAsync([stashed]);
        Assert.Equal(GitChangeState.Committed, (await LoadAsync(harness, stashed)).Thread.GitState);
        Git(harness, "push", "--quiet", "origin", "main");
        await tracker.RefreshThreadsAsync([stashed]);
        Assert.Equal(GitChangeState.Pushed, (await LoadAsync(harness, stashed)).Thread.GitState);

        // Reverted in HEAD's own history: discarded, not "on another branch".
        Git(harness, "revert", "--no-edit", "HEAD");
        await tracker.RefreshThreadsAsync([stashed]);
        Assert.Equal(GitChangeState.Discarded, (await LoadAsync(harness, stashed)).Thread.GitState);

        // Committed, then dropped by a reset while no branch holds the commit.
        var dropped = await ThreadWritesAsync(harness, File1, Changed);
        Git(harness, "commit", "--quiet", "-am", "Halo again");
        await tracker.RefreshThreadsAsync([dropped]);
        Assert.Equal(GitChangeState.Committed, (await LoadAsync(harness, dropped)).Thread.GitState);
        Git(harness, "reset", "--quiet", "--hard", "HEAD~1");
        await tracker.RefreshThreadsAsync([dropped]);
        Assert.Equal(GitChangeState.Lost, (await LoadAsync(harness, dropped)).Thread.GitState);
    }

    [Fact]
    public async Task A_commit_with_the_developers_edits_on_top_is_found_by_the_threads_lines()
    {
        await using var harness = await CreateAsync();
        var threadId = await ThreadWritesAsync(harness, File1, Changed,
            $"--- a/{File1}\n+++ b/{File1}\n@@ block 1 @@\n-{Original.TrimEnd()}\n+{Changed.TrimEnd()}\n");
        harness.Project.Write(File1, "// Reviewed by the team\n" + Changed);
        var tracker = harness.Service<IGitChangeTracker>();

        await tracker.RefreshThreadsAsync([threadId]);
        Assert.Equal(GitChangeState.ModifiedOutside, (await LoadAsync(harness, threadId)).Thread.GitState);

        Git(harness, "commit", "--quiet", "-am", "Halo, reviewed");
        await tracker.RefreshThreadsAsync([threadId]);
        var (thread, file) = await LoadAsync(harness, threadId);
        Assert.Equal(GitChangeState.Committed, thread.GitState);
        Assert.True(file.GitEdited);
        Assert.Equal(Git(harness, "rev-parse", "HEAD"), file.GitCommit);
    }

    [Fact]
    public async Task Another_clone_sees_the_shared_change_on_another_branch_until_it_is_squash_merged()
    {
        await using var harness = await CreateAsync();
        using var remote = new TempWorkspace();
        using var clone = new TempWorkspace();
        GitIn(remote.Root, "init", "--bare", "--quiet", "--initial-branch=main");
        Git(harness, "remote", "add", "origin", remote.Root);
        Git(harness, "push", "--quiet", "origin", "main");
        Git(harness, "checkout", "--quiet", "-b", "feature/halo");
        var threadId = await ThreadWritesAsync(harness, File1, Changed);
        Git(harness, "commit", "--quiet", "-am", "Halo");
        Git(harness, "push", "--quiet", "origin", "feature/halo");
        var tracker = harness.Service<IGitChangeTracker>();
        await tracker.RefreshThreadsAsync([threadId]);

        // The row other developers read.
        await using (var shared = await harness.Service<AIThread.Data.Memory.LongMemoryStore>().CreateGitAsync())
        {
            var row = await shared.ThreadChanges.SingleAsync(c => c.Id == threadId);
            Assert.Equal((ProjectKey, GitChangeState.Pushed, "AIT Test <ait@test.local>"), (row.ProjectKey, row.State, row.GitAuthor));
            Assert.Contains(Git(harness, "rev-parse", "HEAD"), row.Commits);
            Assert.Contains(Git(harness, "hash-object", File1), row.Files);
        }

        // A second clone of the same repository, on main.
        GitIn(Path.GetDirectoryName(clone.Root)!, "clone", "--quiet", remote.Root, clone.Root);
        var cloneId = Guid.NewGuid();
        await using (var db = await harness.CreateDbAsync())
        {
            db.Projects.Add(new Project { Id = cloneId, Name = "Demo (clone)", RootPath = clone.Root, ProjectKey = ProjectKey });
            await db.SaveChangesAsync();
        }
        var presence = await tracker.PresenceAsync(cloneId, [threadId]);
        Assert.Equal(MemoryGitPlace.OtherBranch, presence[threadId].Place);
        Assert.Equal("not in this branch: committed on feature/halo by AIT Test", GitNotes.ForPlanner(presence[threadId]));

        GitIn(clone.Root, "merge", "--quiet", "--squash", "origin/feature/halo");
        GitIn(clone.Root, "commit", "--quiet", "-m", "Squash feature/halo");
        presence = await tracker.PresenceAsync(cloneId, [threadId]);
        Assert.Equal(MemoryGitPlace.Here, presence[threadId].Place);
        Assert.Null(GitNotes.ForPlanner(presence[threadId]));

        // The project's own clone answers from its own state.
        Assert.Equal(MemoryGitPlace.Here, (await tracker.PresenceAsync(harness.ProjectId, [threadId]))[threadId].Place);
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
        Assert.Equal(GitChangeState.Committed, GitChangeTracker.Summarize([(GitChangeState.Pushed, "a"), (GitChangeState.Committed, "b")]).State);
        Assert.Equal(GitChangeState.Stashed, GitChangeTracker.Summarize([(GitChangeState.Stashed, null), (GitChangeState.OnOtherBranch, "b")]).State);
        Assert.Equal(GitChangeState.Lost, GitChangeTracker.Summarize([(GitChangeState.Lost, "a"), (GitChangeState.Discarded, null)]).State);
    }

    private static async Task<OrchestrationHarness> CreateAsync()
    {
        var harness = await OrchestrationHarness.CreateAsync();
        Git(harness, "init", "--initial-branch=main", "--quiet");
        await using (var db = await harness.CreateDbAsync())
            await db.Projects.Where(p => p.Id == harness.ProjectId).ExecuteUpdateAsync(p => p.SetProperty(x => x.ProjectKey, ProjectKey));
        Git(harness, "config", "user.name", "AIT Test");
        Git(harness, "config", "user.email", "ait@test.local");
        Git(harness, "config", "commit.gpgsign", "false");
        Git(harness, "config", "core.autocrlf", "false");
        harness.Project.Write(File1, Original);
        Git(harness, "add", "--all");
        Git(harness, "commit", "--quiet", "-m", "Initial");
        return harness;
    }

    // A finished thread that wrote one file through the file journal, as the executor does; diff = its task's diff.
    private static async Task<Guid> ThreadWritesAsync(OrchestrationHarness harness, string path, string content, string? diff = null)
    {
        var origin = await harness.Service<IGitChangeTracker>().ReadOriginAsync(harness.Project.Root);
        var thread = new ThreadRun
        {
            ProjectId = harness.ProjectId, Prompt = "Ganti sapaan", Status = ThreadStatus.Completed, CompletedAt = DateTime.UtcNow,
            GitBranch = origin?.Branch, GitBaseCommit = origin?.Commit, GitAuthor = origin?.Author
        };
        await using (var db = await harness.CreateDbAsync())
        {
            db.Threads.Add(thread);
            if (diff is not null)
                db.ThreadTasks.Add(new ThreadTask
                {
                    ThreadRunId = thread.Id, TaskKey = "T1", Title = "Halo", FilePath = path, Status = ThreadTaskStatus.Succeeded, Diff = diff
                });
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

    private static string Git(OrchestrationHarness harness, params string[] arguments) => GitIn(harness.Project.Root, arguments);

    // Fixed identity and no signing or line ending conversion, whatever the machine's git config says.
    private static string GitIn(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string[] config = ["-c", "user.name=AIT Test", "-c", "user.email=ait@test.local", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];
        foreach (var argument in config.Concat(arguments)) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Trim();
    }
}

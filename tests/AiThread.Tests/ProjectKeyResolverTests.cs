using AiThread.Common.Enums;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class ProjectKeyResolverTests
{
    [Theory]
    [InlineData("https://github.com/Org/EEP.git", "github.com/org/eep")]
    [InlineData("https://dev:ghp_secret@github.com/org/eep", "github.com/org/eep")]
    [InlineData("git@github.com:org/eep.git", "github.com/org/eep")]
    [InlineData("ssh://git@github.com:22/org/eep.git/", "github.com/org/eep")]
    [InlineData("https://org@dev.azure.com/org/Sales/_git/EEP", "dev.azure.com/org/sales/_git/eep")]
    [InlineData("git@ssh.dev.azure.com:v3/org/Sales/EEP", "dev.azure.com/org/sales/_git/eep")]
    [InlineData("https://gitlab.company.local/group/sub/eep.git", "gitlab.company.local/group/sub/eep")]
    public void Remote_urls_of_one_repository_normalize_to_one_key(string url, string expected) =>
        Assert.Equal(expected, ProjectKeyResolver.NormalizeRemoteUrl(url));

    [Theory]
    [InlineData(@"C:\repos\eep")]
    [InlineData("D:/repos/eep.git")]
    [InlineData("file:///C:/repos/eep.git")]
    [InlineData("")]
    public void Local_path_remotes_are_not_a_shareable_identity(string url) =>
        Assert.Null(ProjectKeyResolver.NormalizeRemoteUrl(url));

    [Fact]
    public void Credentials_never_end_up_in_the_key()
    {
        var key = ProjectKeyResolver.NormalizeRemoteUrl("https://user:p%40ss@github.com/org/eep.git");
        Assert.DoesNotContain("user", key);
        Assert.DoesNotContain("p@ss", key);
    }

    [Fact]
    public void Origin_remote_wins_over_other_remotes()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(".git/config", """
            [core]
                bare = false
            [remote "upstream"]
                url = https://github.com/upstream/eep.git
            [remote "origin"]
                url = git@github.com:org/eep.git
                fetch = +refs/heads/*:refs/remotes/origin/*
            """);

        var key = ProjectKeyResolver.Resolve(workspace.Root, null);

        Assert.Equal(new ProjectKeyInfo("github.com/org/eep", ProjectKeySource.GitRemote), key);
    }

    [Fact]
    public void Override_file_wins_over_manual_key_and_remote()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(".git/config", "[remote \"origin\"]\n\turl = https://github.com/org/eep.git\n");
        workspace.Write(".aithread/project.json", """{ "key": " EEP Core ", "name": "EEP" }""");

        Assert.Equal(new ProjectKeyInfo("eep-core", ProjectKeySource.File), ProjectKeyResolver.Resolve(workspace.Root, "manual"));
    }

    [Fact]
    public void Manual_key_wins_over_remote()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(".git/config", "[remote \"origin\"]\n\turl = https://github.com/org/eep.git\n");

        Assert.Equal(new ProjectKeyInfo("eep", ProjectKeySource.Manual), ProjectKeyResolver.Resolve(workspace.Root, "EEP"));
    }

    [Fact]
    public void Worktree_reads_the_remote_of_its_main_repository()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("main/.git/config", "[remote \"origin\"]\n\turl = https://github.com/org/eep.git\n");
        workspace.Write("main/.git/worktrees/feature/commondir", "../..\n");
        var worktree = Path.Combine(workspace.Root, "feature");
        workspace.Write("feature/.git", $"gitdir: {Path.Combine(workspace.Root, "main", ".git", "worktrees", "feature")}\n");

        Assert.Equal("github.com/org/eep", ProjectKeyResolver.Resolve(worktree, null).Key);
    }

    [Fact]
    public void Without_remote_the_key_is_local_and_stable()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(".git/config", "[core]\n\tbare = false\n");

        var first = ProjectKeyResolver.Resolve(workspace.Root, null);
        var second = ProjectKeyResolver.Resolve(workspace.Root + Path.DirectorySeparatorChar, null);

        Assert.Equal(ProjectKeySource.Local, first.Source);
        Assert.True(ProjectKeyResolver.IsLocal(first.Key));
        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void Broken_override_file_falls_back_to_the_remote()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(".git/config", "[remote \"origin\"]\n\turl = https://github.com/org/eep.git\n");
        workspace.Write(".aithread/project.json", "{ not json");

        Assert.Equal(ProjectKeySource.GitRemote, ProjectKeyResolver.Resolve(workspace.Root, null).Source);
    }
}

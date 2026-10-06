using AiThread.Service.Workspace;

namespace AiThread.Tests;

public class GitIdentityTests
{
    [Fact]
    public void Reads_the_name_of_the_user_section()
    {
        var name = GitIdentity.UserNameIn("""
            [core]
                name = not-this
            [user]
                email = andi@example.com
                name = "Andi Firdaus"
            [alias]
                st = status
            """.Split('\n'));

        Assert.Equal("Andi Firdaus", name);
    }

    [Fact]
    public void Missing_or_empty_name_is_null()
    {
        Assert.Null(GitIdentity.UserNameIn(["[user]", "  email = a@b.c"]));
        Assert.Null(GitIdentity.UserNameIn(["[user]", "  name ="]));
        Assert.Null(GitIdentity.UserNameIn(["# name = commented", "[core]"]));
    }
}

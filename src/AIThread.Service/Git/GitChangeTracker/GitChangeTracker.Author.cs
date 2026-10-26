namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // "Name <email>" as git writes an author, or the part that is set.
    public static string? Author(string? name, string? email) => (name, email) switch
    {
        (null, null) => null,
        (_, null) => name,
        (null, _) => $"<{email}>",
        _ => $"{name} <{email}>"
    };
}

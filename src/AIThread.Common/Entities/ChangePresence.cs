namespace AIThread.Common.Entities;

// A thread's change (ThreadChange) seen in an environment's ref: CreatedAt is when an AIT first saw it there, Commit the
// commit of the environment that holds it (a squash or merge commit when the thread's own commits are not there),
// SeenBy the git identity of that AIT. Any developer's AIT that fetches can record it; once there it stays.
public class ChangePresence : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;
    public Guid ThreadId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string? Commit { get; set; }
    public string? SeenBy { get; set; }
}

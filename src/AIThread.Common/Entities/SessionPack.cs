namespace AIThread.Common.Entities;

// Context pack of a Claude Code session the developer cleared with /clear, written by the context keeper from the
// session transcript and handed to the session that replaced it (SessionEnd / SessionStart hooks).
public class SessionPack : BaseEntity
{
    // The cleared session this pack summarizes.
    public string SessionId { get; set; } = string.Empty;

    // Normalized working directory: the new session is matched to the cleared one through it.
    public string WorkingDirectory { get; set; } = string.Empty;

    // Null while the context keeper is still writing it.
    public string? Pack { get; set; }

    // The pack the cleared session itself started from, so consecutive /clear calls build on each other.
    public Guid? PreviousPackId { get; set; }

    public string? HandedToSessionId { get; set; }
    public DateTime? HandedAt { get; set; }
}

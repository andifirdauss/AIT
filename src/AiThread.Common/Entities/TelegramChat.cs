namespace AiThread.Common.Entities;

// A private Telegram chat that has messaged the bot. Only allowed chats may run prompts; the others wait for
// approval on the Setting page.
public class TelegramChat : BaseEntity
{
    public long ChatId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public bool IsAllowed { get; set; }

    // Project that plain-text prompts from this chat run against.
    public Guid? ActiveProjectId { get; set; }
    public Project? ActiveProject { get; set; }

    // Threads started before this moment are not sent to the planner as earlier conversation (/new, project switch).
    public DateTime ConversationStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

namespace AIThread.Common.Entities;

// An image attached to the prompt of a thread (a screenshot pasted into Claude Code, Codex, …), kept in the main
// database until the business wiki writer has summarized the thread, so it can end up on a business page.
public class ThreadAttachment : BaseEntity
{
    public Guid ThreadRunId { get; set; }
    public ThreadRun? ThreadRun { get; set; }

    public string MediaType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public string Sha256 { get; set; } = string.Empty;
}

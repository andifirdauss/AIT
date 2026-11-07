using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// A commit made outside AIT that an AIT took on (CommitIngestor). The row is the claim: the unique (ProjectKey, Commit)
// keeps two developers' AIT from summarizing the same commit. Its Id is also the id of its ThreadChange row and the
// SourceThreadId of the memory written from it, so git state, environments and the wiki treat it like a thread.
public class IngestedCommit : BaseEntity
{
    public const int MaxDigestLength = 8_000;

    public string ProjectKey { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public IngestStatus Status { get; set; }

    // First line of the commit message, author "Name <email>", commit time.
    public string? Title { get; set; }
    public string? Author { get; set; }
    public DateTime? CommittedAt { get; set; }

    // The AIT that took it on ("name@machine"); only that one hands it to its business wiki writer.
    public string? ClaimedBy { get; set; }
    public Guid? MemoryId { get; set; }

    // What the business wiki writer reads about the commit; set once summarized.
    public string? Digest { get; set; }
    public DateTime? BusinessDigestedAt { get; set; }

    // Why it was skipped or failed.
    public string? Note { get; set; }

    // git patch-id --stable of the commit: a rebase or cherry-pick of a summarized commit has the same one, and is
    // recorded as a copy of it instead of being summarized again.
    public string? PatchId { get; set; }

    // The working branch the commit was read from (not in the ingest ref yet when taken on); null = read from the ref.
    public string? Branch { get; set; }
}

namespace AIThread.Common.Entities;

// Whether commits made outside AIT are summarized into the project's memory and business wiki (CommitIngestor), shared
// by every developer of the project key. Costs tokens, so it is off until someone turns it on. Ref = the branch whose
// commits are read (null: the production environment's ref, else origin/HEAD); Since = only commits after it (set when
// turned on, so years of history are not summarized at once); MaxPerRun = commits per pass.
public class CommitIngestSettings : BaseEntity
{
    public const int DefaultMaxPerRun = 10;
    public const int MaxMaxPerRun = 50;

    public string ProjectKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Ref { get; set; }
    public DateTime Since { get; set; } = DateTime.UtcNow;
    public int MaxPerRun { get; set; } = DefaultMaxPerRun;
    public string? UpdatedBy { get; set; }
}

namespace AIThread.Common.Enums;

// Where a commit made outside AIT is in CommitIngestor. Stored by name.
public enum IngestStatus
{
    // Claimed, being summarized.
    Pending = 1,

    // Summarized into memory (and waiting for or done with the business wiki).
    Done = 2,

    // Nothing to keep (an AIT change, only files git or AIT ignore, or the model found nothing).
    Skipped = 3,

    // The model or the store failed; not tried again automatically.
    Failed = 4
}

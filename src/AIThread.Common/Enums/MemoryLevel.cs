namespace AIThread.Common.Enums;

public enum MemoryLevel
{
    // Stored in Redis, lives for the configured short-term window (default 1 day).
    Short = 1,

    // Stored in the database after consolidation.
    Long = 2,

    // The business wiki (business memory): its own store, never consolidated.
    Business = 3
}

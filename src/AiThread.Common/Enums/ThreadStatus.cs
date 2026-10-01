namespace AiThread.Common.Enums;

public enum ThreadStatus
{
    Pending = 0,
    Planning = 1,
    Executing = 2,
    Wrapping = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,

    // Building the project and/or reviewing the changes, possibly with fix rounds; between Executing and Wrapping.
    Verifying = 7
}

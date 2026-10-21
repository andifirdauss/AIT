namespace AIThread.Common.Enums;

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
    Verifying = 7,

    // The planner asked the developer a question (ask_user) and the thread waits for the next prompt of the same
    // session, which resumes planning where it stopped.
    AwaitingInput = 8,

    // The file changes of the thread were undone (rollback); it cannot be retried any more.
    RolledBack = 9,

    // The tester node uses the running app to check the change (Auto Testing); between Verifying and Wrapping.
    Testing = 10
}

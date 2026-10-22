namespace AIThread.Common.Enums;

// The outcome of one run of a test. Skipped always comes with a reason (the environment lacks what the test needs, the
// test skips itself, the runner gives no result per test); NotFound means the runner did not report the test at all.
public enum UnitTestResult
{
    Unknown = 0,
    Passed = 1,
    Failed = 2,
    Error = 3,
    Skipped = 4,
    NotFound = 5
}

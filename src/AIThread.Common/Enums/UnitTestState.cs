namespace AIThread.Common.Enums;

// Where a test of the unit test catalog stands. Only Active tests run; Quarantined (unstable) and Obsolete (proposed for
// removal, waiting for the developer) are kept but skipped; Removed stays as history so the test is never restored again.
public enum UnitTestState
{
    Active = 1,
    Quarantined = 2,
    Obsolete = 3,
    Removed = 4
}

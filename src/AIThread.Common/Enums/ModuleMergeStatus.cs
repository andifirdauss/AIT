namespace AIThread.Common.Enums;

public enum ModuleMergeStatus
{
    // Found by the duplicate check but not certain enough to apply: waits in Memory → Perlu review.
    Suggested = 1,

    Applied = 2,
    Undone = 3,
    Rejected = 4
}

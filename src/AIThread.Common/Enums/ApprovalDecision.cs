namespace AIThread.Common.Enums;

// The developer's answer to an approval request: this one time, from now on (saved as an ApprovalGrant), or no.
public enum ApprovalDecision
{
    Once = 1,
    Always = 2,
    Deny = 3
}

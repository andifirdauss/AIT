namespace AIThread.Common.Enums;

// The developer's answer to an approval request: this one time, from now on (saved as an ApprovalGrant), or no.
// AlwaysPattern: from now on for a command pattern ("npm install *", ApprovalItem.Pattern) instead of the exact command.
public enum ApprovalDecision
{
    Once = 1,
    Always = 2,
    Deny = 3,
    AlwaysPattern = 4
}

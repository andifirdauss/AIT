namespace AIThread.Common.Enums;

// The developer's answer to an approval request: this one time, from now on (saved as an ApprovalGrant), or no.
// AlwaysPattern: from now on for a command pattern ("npm install *", ApprovalItem.Pattern) instead of the exact command.
// AlwaysAll: from now on for every command of the project (grant ApprovalTargets.AllCommands); credential files stay refused.
public enum ApprovalDecision
{
    Once = 1,
    Always = 2,
    Deny = 3,
    AlwaysPattern = 4,
    AlwaysAll = 5
}

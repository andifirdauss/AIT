namespace AIThread.Common.Enums;

// What AIT asks the developer's permission for before doing it. Stored by name.
public enum ApprovalKind
{
    // Writing files outside the project root; the target is the folder the files live in (their repository or project
    // root when there is one).
    WriteOutside = 1,

    // A shell command that is not on the read-only allowlist (run_command); the target is the command text.
    Command = 2,

    // A read-only HTTP call (run_command curl) to a host that is not this machine; the target is host:port.
    Host = 3,

    // Committing the thread's files; the target is the repository root.
    Commit = 4,

    // Pushing the branch; the target is "<repository>|<remote>/<branch>".
    Push = 5
}

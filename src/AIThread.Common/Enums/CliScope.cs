namespace AIThread.Common.Enums;

// Where a coding CLI's configuration is written to point it at AIThread.
public enum CliScope
{
    // The CLI's user-level config — every session of that CLI on this machine goes through AIThread.
    Global = 1,

    // A file inside a registered project — only sessions started in that project.
    Project = 2
}

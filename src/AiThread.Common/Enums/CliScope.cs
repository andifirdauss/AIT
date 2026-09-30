namespace AiThread.Common.Enums;

// Where a coding CLI's configuration is written to point it at AiThread.
public enum CliScope
{
    // The CLI's user-level config — every session of that CLI on this machine goes through AiThread.
    Global = 1,

    // A file inside a registered project — only sessions started in that project.
    Project = 2
}

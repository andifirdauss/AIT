namespace AIThread.Common.Enums;

public enum ClaudeSettingsScope
{
    // ~/.claude/settings.json — every Claude Code session goes through AIThread.
    User = 1,

    // <project>/.claude/settings.local.json — only sessions opened in that project.
    Project = 2
}

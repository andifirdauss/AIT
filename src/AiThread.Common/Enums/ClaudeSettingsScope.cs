namespace AiThread.Common.Enums;

public enum ClaudeSettingsScope
{
    // ~/.claude/settings.json — every Claude Code session goes through AiThread.
    User = 1,

    // <project>/.claude/settings.local.json — only sessions opened in that project.
    Project = 2
}

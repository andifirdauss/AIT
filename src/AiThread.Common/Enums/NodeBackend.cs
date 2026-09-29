namespace AiThread.Common.Enums;

// How a node reaches its model.
public enum NodeBackend
{
    // OpenAI-compatible chat through 9router.
    Router = 0,

    // The vendor's own Claude Code CLI (`claude -p`), logged in on this machine. Planner only.
    ClaudeCli = 1
}

namespace AiThread.Common.Enums;

public enum NodeRole
{
    Planner = 1,
    Executor = 2,
    Wrapper = 3,

    // Optional: keeps the running summary of a Claude Code conversation that every planner run starts from.
    ContextKeeper = 4
}

namespace AIThread.Common.Enums;

public enum NodeRole
{
    Planner = 1,
    Executor = 2,
    Wrapper = 3,

    // Optional: keeps the running summary of a Claude Code conversation that every planner run starts from.
    ContextKeeper = 4,

    // Optional: checks the executors' changes against the plan (and the build result) before the wrapper reports.
    Reviewer = 5,

    // Optional: a cheap investigator the planner hands its delegated tools to (PlannerToolDelegation); it runs them in
    // its own loop and returns short findings, so the raw tool output never enters the planner's conversation.
    ToolsNode = 6,

    // Optional: with Auto Testing on the planner node, checks the verified change on the running app the way the plan's
    // test section describes — pages in a visible browser (Playwright), endpoints with HTTP requests.
    Tester = 7
}

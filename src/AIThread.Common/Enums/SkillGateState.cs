namespace AIThread.Common.Enums;

// What the Claude Code plugin's hook learns about a folder from a running AIT.
public enum SkillGateState
{
    // Registered, and the pipeline has its nodes: code changes in this folder go through AIT.
    Ready = 0,

    // A project folder, but the pipeline has no complete set of nodes yet.
    SetupIncomplete = 1,

    // A project folder AIT does not know yet.
    ProjectNotRegistered = 2,

    // Not a project folder at all: the hook has nothing to say.
    NotAProject = 3
}

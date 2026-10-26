namespace AIThread.Common.Enums;

// What kind of fact a memory states: the last part of its label. Pitfall..Pattern are the facts of older versions,
// chosen by the ordered questions in AgentPrompts.MemoryLabelRules; ModuleSummaryWriter folds them into the Summary of
// their module. New project memory is a Change entry per thread plus that Summary.
public enum MemoryKind
{
    // A mistake or trap to avoid, with its fix.
    Pitfall = 1,

    // A business condition the system enforces: what is allowed, required or calculated (validation included).
    Rule = 2,

    // A sequence of steps across components or time.
    Flow = 3,

    // How developers write code in this project.
    Convention = 4,

    // How the parts are organized and connected, and why.
    Architecture = 5,

    // A reusable technical solution, useful in other projects too.
    Pattern = 6,

    // One entry of the module's change log, written after a thread: the feature that changed, the files the thread
    // changed, and the validations and flows it changed (or, for a thread that changed no file, what it found).
    Change = 7,

    // The current state of one module (features, main files, validations, flows, notes), one per project key and
    // module, long memory only. ModuleSummaryWriter folds the change entries into it once they reach long memory.
    Summary = 8
}

namespace AiThread.Common.Enums;

// What kind of fact a memory states: the last part of its label. Chosen by the ordered questions in
// AgentPrompts.MemoryKindRules (the first "yes" wins), so an ambiguous fact always lands on the same kind.
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
    Pattern = 6
}

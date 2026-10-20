using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

// Nodes are the active planners in priority order; the first one plans, and its fallback chain (AiNode.FallbackNodeId)
// takes over when it is rate limited or down. CanAsk offers ask_user: the developer can answer (setting on, and a session the answer can come back through).
// CodeMap: module and layer of the files the request names, as far as known before planning (the first search layer).
// LabelsFromSession: set when the request names no file and CodeMap comes from the session instead (a follow-up).
// Files: the project files behind CodeMap, also those whose module is not known yet; the brief outlines them.
public sealed record PlannerInput(
    ThreadRequest Request,
    ProjectContext Project,
    IReadOnlyList<AiNode> Nodes,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    bool CanAsk = false,
    IReadOnlyList<Memory.CodeMapHit>? CodeMap = null,
    SessionLabelSource? LabelsFromSession = null,
    IReadOnlyList<string>? Files = null);

// Where the labels of a follow-up came from: the session's last thread (its prompt joins the keyword search), or —
// ThreadId null — the recent conversation or the session summary.
public sealed record SessionLabelSource(Guid? ThreadId, string? Prompt);

// Node is the planner that finished (a fallback when the preferred one failed). Plan is null when the planner paused
// on a question instead (Pause). Steps and tokens are totals for the thread, including the run before a pause.
// MemoriesUsed counts the memories (and wiki pages) the planner read in this run.
// ToolsNodeTokens: spent by the tools nodes the planner delegated tools to (not part of Tokens).
public sealed record PlanningResult(ThreadPlan? Plan, int Steps, int Tokens, int CachedTokens, AiNode Node, PlannerPause? Pause = null, int MemoriesUsed = 0, int ToolsNodeTokens = 0);

public sealed record PlannerPause(PlannerQuestion Question, PlannerConversation Conversation);

public sealed record PlannerToolOutcome(string Output, ThreadPlan? AcceptedPlan, bool IsError = false, PlannerQuestion? Question = null);

// The expensive model explores the repository (read-only) and ends by calling submit_plan, through a 9router
// tool-calling loop. The planner's fallback node continues the same conversation when it is rate limited or down.
// Tools the planner node delegated (AiNode.ToolDelegations) are run by tools nodes behind investigate instead.
public sealed partial class PlannerAgent(
    INodeCaller caller,
    IMemoryService memoryService,
    INodeService nodeService,
    IPlannerToolRunner runner,
    IToolsNodeAgent toolsNodeAgent,
    INodePromptBuilder prompts,
    ICodeSkeletonTools skeleton) : IPlannerAgent
{
    private const int MaxRequestChars = 40_000;
    private const int MaxAttachedContextChars = 30_000;
    private const int MaxTextNudges = 2;
    private const int MaxQuestions = 2;

    // A resumed run always gets a few steps to act on the answer, even when the question came late.
    private const int StepsAfterAnswer = 3;

    // Module·layer labels named in the label line; the rest are counted.
    private const int MaxLabelsShown = 4;

    // Part of the session's last prompt added to a follow-up's keyword search.
    private const int MaxSessionPromptChars = 2_000;

    // Outlines of the files the request names, put in the brief so the planner reads only the lines it needs.
    private const int MaxOutlinedFiles = 6;
    private const int MaxOutlineChars = 12_000;

    // Step is the last step taken; MaxSteps is when the planner is told to submit (one forced step may follow).
    // Investigations and ToolsNodeTokens are fields: parallel investigate calls update them with Interlocked.
    private sealed class PlannerRun
    {
        public int Investigations;
        public int ToolsNodeTokens;

        public required JsonArray Messages { get; init; }
        public required AiNode Node { get; set; }
        public int Step { get; set; }
        public int Tokens { get; set; }
        public int CachedTokens { get; set; }
        public int QuestionsAsked { get; set; }
        public bool CanBuild { get; init; }
        public int MaxSteps { get; init; }
        public IReadOnlyList<ToolsNodeGroup> Groups { get; init; } = [];
        public HashSet<Guid> MemoriesRead { get; } = [];
    }
}

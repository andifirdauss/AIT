using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

public sealed record ReviewIssue(string File, string? Anchor, string Problem, string Fix);

// Approved = nothing to fix. Error is set when the reviewer could not be asked or answered unreadably; the review
// then counts as not done, never as a reason to change code.
public sealed record ReviewResult(bool Approved, string? Summary, IReadOnlyList<ReviewIssue> Issues, int Tokens, string? Error = null)
{
    public static ReviewResult Failed(string error) => new(true, null, [], 0, error);
}

// One call: the request, the plan and contract, every task's diff and the build result in; the problems that break
// the request out, each phrased as an instruction an executor can apply to one file.
public sealed partial class ReviewerAgent(INodeCaller caller, INodePromptBuilder prompts) : IReviewerAgent
{
    public const int MaxIssues = 20;
    private const int MaxDiffLinesPerTask = 150;
    private const int MaxInstructionChars = 1500;
}

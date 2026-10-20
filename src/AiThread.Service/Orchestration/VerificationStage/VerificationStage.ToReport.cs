using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    private static BuildRoundReport ToReport(BuildResult build, WorkspaceScope scope) => new()
    {
        Status = build.StartError is not null ? "error"
            : build.TimedOut ? "timeout"
            : build.Succeeded ? "passed"
            : build.OnlyLockedOutput ? "locked"
            : "failed",
        Headline = BuildReport.Headline(build),
        ErrorCount = build.Errors.Count,
        WarningCount = build.WarningCount,
        DurationMs = build.DurationMs,
        Errors = build.Errors.Take(MaxErrorsInReport).Select(e => BuildReport.Line(e, scope)).ToList(),
        Output = build.Passed || build.Errors.Count > 0 || string.IsNullOrWhiteSpace(build.OutputTail) ? null
            : build.OutputTail.Length <= MaxOutputChars ? build.OutputTail : "…" + build.OutputTail[^MaxOutputChars..]
    };
}

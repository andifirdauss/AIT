using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// One run of one catalog test: the history behind LastResult, kept for the latest runs only. Phase is baseline (before a
// thread's change), after (after it), manual (Project > Detail) or flaky (failed and then passed on the retry).
public class UnitTestRun : BaseEntity
{
    public const string Baseline = "baseline";
    public const string After = "after";
    public const string Manual = "manual";
    public const string Flaky = "flaky";

    public Guid UnitTestCaseId { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public Guid? ThreadId { get; set; }
    public string Phase { get; set; } = Manual;
    public UnitTestResult Result { get; set; }
    public long? DurationMs { get; set; }
    public string? Message { get; set; }
    public string? SkipReason { get; set; }
    public string? Machine { get; set; }
    public string? RunBy { get; set; }
}

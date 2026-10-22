using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// One test of a project's unit test catalog. Kept with long memory, keyed by ProjectKey, so every developer of the project
// works with the same list: the next developer runs the tests of the previous one before changing code they do not know.
// The code of the test lives in the repository; the catalog holds what it guards, where it is, how to run it, its last
// result and (internally, never shown) a snapshot of its source so a missing test can be put back.
public class UnitTestCase : BaseEntity
{
    public const int MaxPathLength = 400;
    public const int MaxSelectorLength = 600;

    public string ProjectKey { get; set; } = string.Empty;
    public string? ProjectName { get; set; }

    // Shown as UT-0001; counted per project and never reused, not even after the test is removed.
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    // The rule or behaviour the test guards, in one or two sentences.
    public string? Purpose { get; set; }

    // The case as data: starting state, input, expected result. What a test is written again from when nothing else is left.
    public string? Scenario { get; set; }

    // Label like a memory: module, layers (comma-separated MemoryLayer names) and keywords (comma-separated).
    public string? Module { get; set; }
    public string? Layers { get; set; }
    public string? Keywords { get; set; }

    // The code the test exercises: relative paths (one per line) and symbols (comma-separated). Selects the tests a
    // change touches and tells the planner which tests a change concerns.
    public string? TargetFiles { get; set; }
    public string? TargetSymbols { get; set; }

    // xunit, nunit, mstest, jest, vitest, mocha, pytest, phpunit, junit, go, rust, npm.
    public string Framework { get; set; } = string.Empty;

    // Manifest of the suite the test runs in (test .csproj, package.json, pom.xml, ...), relative to the root.
    public string? TestProject { get; set; }

    public string TestFile { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public string MethodName { get; set; } = string.Empty;

    // What the runner is given to run just this test: Namespace.Class.Method, a pytest node id, file::title for jest, ...
    public string Selector { get; set; } = string.Empty;

    // What the test needs besides the code, comma-separated: database, network, config. Filled when a run fails for
    // lack of it, so the next run on a machine without it is skipped at once instead of waiting for a timeout.
    public string? Requires { get; set; }

    public UnitTestState State { get; set; } = UnitTestState.Active;
    public UnitTestOrigin Origin { get; set; } = UnitTestOrigin.Ait;

    public string? CreatedBy { get; set; }
    public Guid? CreatedThreadId { get; set; }

    // Set when a thread proposes removing the test; the developer confirms (Removed) or restores it (Active).
    public string? ObsoleteReason { get; set; }
    public Guid? ObsoleteThreadId { get; set; }
    public string? ObsoleteBy { get; set; }
    public DateTime? ObsoleteAt { get; set; }

    // Why the test is quarantined (it passes and fails without a change).
    public string? QuarantineReason { get; set; }

    public string? RemovedBy { get; set; }
    public DateTime? RemovedAt { get; set; }

    public UnitTestResult LastResult { get; set; } = UnitTestResult.Unknown;
    public DateTime? LastRunAt { get; set; }
    public string? LastRunBy { get; set; }
    public long? LastDurationMs { get; set; }
    public string? LastMessage { get; set; }

    // Always set when LastResult is Skipped.
    public string? LastSkipReason { get; set; }

    // Hash of the test file and the target files when LastResult was recorded: the same code gives the same result, so
    // a baseline may use it instead of running the test again.
    public string? LastInputHash { get; set; }

    // Internal: the test file as it was when the test last passed, for putting it back when the file is missing. Never
    // shown in the UI or returned by the API; only the memory export carries it.
    public string? SourceSnapshot { get; set; }
    public string? SourceHash { get; set; }
    public string? SourceCommit { get; set; }
    public string? SourceBranch { get; set; }

    // Internal: the test method alone, for putting it back into a test file that exists without it.
    public string? MethodSnapshot { get; set; }

    // Internal: JSON [{ "path": ..., "content": ... }] of the files the test needs besides itself (test project file,
    // conftest.py, ...), restored only when they are missing.
    public string? SupportFiles { get; set; }

    public string DisplayNumber => FormatNumber(Number);

    public static string FormatNumber(int number) => $"UT-{number:0000}";
}

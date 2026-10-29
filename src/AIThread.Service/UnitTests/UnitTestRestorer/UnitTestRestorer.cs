using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

// How one test came back: moved (found in another file), snapshot (its file was written back), method (its method was
// put back into its file), task (an executor puts it back), regenerate (an executor writes it again from its scenario)
// or missing (nothing to restore it from).
public sealed record UnitTestRestoreItem(string Number, string File, string How, string? Note = null)
{
    public const string Moved = "moved";
    public const string Snapshot = "snapshot";
    public const string Method = "method";
    public const string Task = "task";
    public const string Regenerate = "regenerate";
    public const string Missing = "missing";
}

// Uncommitted: the missing tests left out because no commit known here ever held them.
public sealed record UnitTestRestoreResult(IReadOnlyList<UnitTestRestoreItem> Items, IReadOnlyList<PlanTask> Tasks, IReadOnlyList<string> Notes,
    IReadOnlyList<Guid> Uncommitted)
{
    public static readonly UnitTestRestoreResult None = new([], [], [], []);
}

// Before a thread plans, the tests of the catalog that this repository lacks are put back automatically: they are part
// of the project's standard code. A test no commit here ever held is left out (it lived only in some working tree, with
// the code it tests). Layer 1 tells where the original is in git, layer 2 restores from the snapshot without a model,
// layer 3 has an executor write the test again from its scenario.
public sealed partial class UnitTestRestorer(IUnitTestCatalog catalog, IUnitTestScanner scanner, ILogger<UnitTestRestorer> logger) : IUnitTestRestorer
{
    private static readonly TimeSpan SolutionTimeout = TimeSpan.FromSeconds(60);

    // Lines of an existing test file the executor sees as the style to follow.
    private const int ExampleLines = 120;
}

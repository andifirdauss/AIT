using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The suites whose project the verification build has just compiled: running them does not need a second build.
    private static IReadOnlyList<TestSuite> WithBuilt(IReadOnlyList<TestSuite> suites, IReadOnlyList<string>? builtProjects) =>
        builtProjects is not { Count: > 0 }
            ? suites
            : suites.Select(s => builtProjects.Contains(Path.GetFullPath(Path.Combine(s.Root, s.Manifest)), StringComparer.OrdinalIgnoreCase) ? s with { AlreadyBuilt = true } : s).ToList();
}

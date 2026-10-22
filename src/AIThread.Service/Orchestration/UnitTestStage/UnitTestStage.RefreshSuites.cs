using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // A test project the executors created joins before the new tests are looked up: the suites are read again from the
    // manifests on disk when the project had none, or when a new test sits outside every known suite.
    private static void RefreshSuites(UnitTestSession session, ThreadPlan plan)
    {
        var root = session.Project.RootPath;
        var newTests = plan.Tests?.New ?? [];
        if (session.Suites.Count > 0 && newTests.All(n => TestCommandResolver.SuiteFor(session.Suites, root, n.File) is not null)) return;

        var modules = ProjectLocator.FindManifests(root).Select(m => m.RelativePath).ToList();
        var suites = TestCommandResolver.Suites(root, modules);
        if (suites.Select(s => s.Manifest).SequenceEqual(session.Suites.Select(s => s.Manifest), StringComparer.OrdinalIgnoreCase)) return;

        session.Suites = suites;
        session.Report.Suites = suites.Where(s => s.CanRun).Select(s => $"{s.Framework} · {s.Manifest}").ToList();
        session.Report.Problems = suites.Where(s => !s.CanRun).Select(s => $"{s.Manifest}: {s.Problem}").ToList();
    }
}

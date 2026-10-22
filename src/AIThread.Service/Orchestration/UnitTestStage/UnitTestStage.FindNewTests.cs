using AIThread.Common.Enums;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The tests the plan adds, looked up by name in their files now that the executors wrote them. A name the file does
    // not hold is noted once; a test already in the catalog is not new.
    private static void FindNewTests(UnitTestSession session, ThreadPlan plan, Guid threadId)
    {
        var root = session.Project.RootPath;
        foreach (var wanted in plan.Tests?.New ?? [])
        {
            if (session.NewTests.Any(n => string.Equals(n.TestFile, wanted.File, StringComparison.OrdinalIgnoreCase) && n.MethodName == wanted.Name)) continue;
            var suite = TestCommandResolver.SuiteFor(session.Suites, root, wanted.File);
            var inFile = suite is null ? [] : TestDiscovery.InFile(root, wanted.File, suite);
            var found = inFile.FirstOrDefault(d => d.MethodName == wanted.Name)
                        ?? inFile.FirstOrDefault(d => string.Equals(d.MethodName, wanted.Name, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                var note = suite is null
                    ? $"Test baru \"{wanted.Name}\" ada di {wanted.File}, yang bukan bagian dari suite test mana pun."
                    : $"Test baru \"{wanted.Name}\" tidak ditemukan di {wanted.File}.";
                if (!session.Report.Notes.Contains(note)) session.Report.Notes.Add(note);
                continue;
            }
            if (session.Tests.Any(t => t.Selector == found.Selector)) continue;

            var entry = UnitTestEntries.FromDiscovered(found, session.Project.Key, session.Project.Name, UnitTestOrigin.Ait, session.Developer, threadId);
            entry.Title = UnitTestText.Clip(wanted.Title, 300);
            entry.Purpose = wanted.Purpose is null ? null : UnitTestText.Clip(wanted.Purpose, 2_000);
            entry.Scenario = UnitTestText.Clip(wanted.Scenario, 4_000);
            entry.TargetFiles = UnitTestText.JoinLines(wanted.Targets);
            entry.Requires = UnitTestText.JoinCsv(wanted.Requires);
            session.NewTests.Add(entry);
        }
    }
}

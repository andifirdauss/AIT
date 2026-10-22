using AIThread.Common.Entities;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // Without a triage the code under test is fixed: the changed files the test targets, else the only changed file
    // that is not a test. Otherwise there is no telling which file broke it.
    private static void MechanicalIssues(UnitTestSession session, UnitTestCheckInput input, UnitTestCase test, string label, UnitTestReportItem item, bool isNew,
        List<ReviewIssue> issues, List<string> unfixable)
    {
        var changedCode = input.ChangedFiles.Where(f => !IsTestFile(session, f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var targets = UnitTestText.Lines(test.TargetFiles).Where(f => changedCode.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0 && changedCode.Count == 1) targets = changedCode;
        if (targets.Count == 0)
        {
            unfixable.Add($"Unit test {item.Number} \"{test.Title}\" fails, but the file to fix cannot be told without the tester's triage: {item.Message}");
            return;
        }
        foreach (var target in targets) issues.Add(new ReviewIssue(target, null, Problem(test, label, item, isNew), CodeFix(test, isNew)));
    }
}

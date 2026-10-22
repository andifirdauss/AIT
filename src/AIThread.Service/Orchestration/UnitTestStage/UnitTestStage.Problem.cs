using AIThread.Common.Entities;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // What an executor reads about a failing test (the instructions of fix tasks are in English).
    private static string Problem(UnitTestCase test, string label, UnitTestReportItem item, bool isNew) =>
        $"Unit test {label} \"{test.Title}\" ({test.TestFile}, {test.MethodName}) " +
        (isNew ? "was written in this change and fails" : "passed before this change and fails now") +
        $": {UnitTestText.Clip(item.Message, MaxMessageChars)}";
}

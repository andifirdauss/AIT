using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    private static string CodeFix(UnitTestCase test, bool isNew) => isNew
        ? $"Change the code so this new test passes; do not change the test. The case it checks: {test.Scenario ?? test.Purpose ?? test.Title}"
        : $"Change the code so this test passes again; do not change the test. It guards: {test.Purpose ?? test.Title}";
}

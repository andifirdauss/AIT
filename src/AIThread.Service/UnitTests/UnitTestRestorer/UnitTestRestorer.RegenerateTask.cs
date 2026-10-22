using System.Text;
using AIThread.Common.Entities;
using AIThread.Service.Orchestration;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // Without any snapshot an executor writes the tests again from the catalog's description: their name, purpose and
    // scenario, in the style of another test file of the same suite.
    private static PlanTask RegenerateTask(string id, string file, bool exists, IReadOnlyList<UnitTestCase> tests, TestSuite? suite, ContextRef? example)
    {
        var builder = new StringBuilder();
        builder.Append(exists
                ? "Add these unit tests of the project's test catalog to this file; they are missing. "
                : "Create this test file with these unit tests of the project's test catalog; the file is missing. ")
            .Append(suite is null ? string.Empty : $"Framework: {suite.Framework}. ")
            .Append("Use the test names exactly as given (the runner finds them by name) and check exactly what each scenario says. ");
        builder.Append(example is null
            ? "Follow the usual conventions of the framework.\n"
            : "Follow the style of the existing test file in the reference excerpt: imports, namespace, naming, setup and assertions.\n");
        if (exists) builder.Append("Change nothing else in the file.\n");
        foreach (var test in tests)
        {
            builder.Append($"\n{test.DisplayNumber} · {test.Title}\n");
            builder.Append($"Name: {test.MethodName}").Append(test.ClassName is null ? string.Empty : $" (class {test.ClassName})").Append('\n');
            if (test.Purpose is not null) builder.Append($"Purpose: {test.Purpose}\n");
            builder.Append($"Scenario: {test.Scenario}\n");
            if (test.TargetFiles is not null) builder.Append($"Code under test: {string.Join(", ", UnitTestText.Lines(test.TargetFiles))}\n");
        }
        return new PlanTask
        {
            Id = id,
            Title = "Write missing unit tests again from the catalog",
            File = file,
            Action = exists ? PlanTask.Edit : PlanTask.Create,
            Instruction = builder.ToString().TrimEnd(),
            Context = example is null ? null : [example]
        };
    }
}

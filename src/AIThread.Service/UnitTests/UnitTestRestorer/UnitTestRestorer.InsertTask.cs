using System.Text;
using AIThread.Common.Entities;
using AIThread.Service.Orchestration;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // An executor puts the methods back verbatim; their source comes from the snapshot.
    private static PlanTask InsertTask(string id, string file, IReadOnlyList<UnitTestCase> tests)
    {
        var builder = new StringBuilder();
        builder.Append("These unit tests of the project's test catalog are missing from this file. Put each of them back exactly as written below, ")
            .Append("next to the other tests of the same class or block, and change nothing else in the file.\n");
        foreach (var test in tests)
        {
            builder.Append($"\n{test.DisplayNumber} · {test.Title}").Append(test.ClassName is null ? string.Empty : $" (class {test.ClassName})").Append(":\n");
            builder.Append("```\n").Append(test.MethodSnapshot!.TrimEnd()).Append("\n```\n");
        }
        return new PlanTask
        {
            Id = id,
            Title = "Restore unit tests from the catalog",
            File = file,
            Action = PlanTask.Edit,
            Instruction = builder.ToString().TrimEnd()
        };
    }
}

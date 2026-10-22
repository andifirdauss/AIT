using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The whole report as the wrapper and the context keeper read it (English).
    public static string Describe(UnitTestReport report)
    {
        var builder = new StringBuilder($"Unit tests: {report.Outcome}");
        if (report.TestOnly) builder.Append(" (only tests were run; nothing changed)");
        builder.Append('\n');
        foreach (var item in report.Restored) builder.Append($"- restored {item.Number} ({item.How}) in {item.File}\n");
        foreach (var item in report.Tests.Where(t => t.After is not null || t.Verdict == UnitTestReportItem.Retired))
        {
            builder.Append($"- {item.Number} \"{item.Title}\": {item.Verdict}");
            if (item.Baseline is not null) builder.Append($" (before: {item.Baseline}, after: {item.After})");
            if (item.SkipReason is not null) builder.Append($" — {item.SkipReason}");
            else if (item.Message is not null && item.Verdict != UnitTestReportItem.Ok) builder.Append($" — {OneLine(item.Message)}");
            builder.Append('\n');
        }
        foreach (var note in report.Notes) builder.Append($"Note: {note}\n");
        return builder.ToString();
    }
}

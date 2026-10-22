using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The report line of a test, created on first use. A new test has no number until the catalog registers it.
    private static UnitTestReportItem Item(UnitTestSession session, UnitTestCase test, bool isNew, string? decision)
    {
        if (!session.Items.TryGetValue(test.Id, out var item))
        {
            item = new UnitTestReportItem
            {
                Id = isNew ? null : test.Id,
                Number = isNew ? "baru" : test.DisplayNumber,
                Title = test.Title,
                File = test.TestFile
            };
            session.Items[test.Id] = item;
            session.Report.Tests.Add(item);
        }
        item.Decision = decision;
        return item;
    }
}

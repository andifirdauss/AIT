using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The file's copy of a test is newer when it was changed or run later than the one here.
    private static bool IsNewer(UnitTestExportItem item, UnitTestCase local)
    {
        var theirs = new[] { Utc(item.UpdatedAt ?? item.CreatedAt), item.LastRunAt is { } run ? Utc(run) : DateTime.MinValue }.Max();
        var ours = new[] { local.UpdatedAt ?? local.CreatedAt, local.LastRunAt ?? DateTime.MinValue }.Max();
        return theirs > ours;
    }
}

using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? InvalidUnitTest(UnitTestExportItem? item)
    {
        if (item is null) return Lang.T("kosong.", "empty.");
        if (item.Id == Guid.Empty) return Lang.T("id kosong.", "empty id.");
        if (string.IsNullOrWhiteSpace(item.ProjectKey) || item.ProjectKey.Length > 300) return Lang.T("project key kosong atau lebih dari 300 karakter.", "the project key is empty or longer than 300 characters.");
        if (string.IsNullOrWhiteSpace(item.Title)) return Lang.T("judul kosong.", "empty title.");
        if (string.IsNullOrWhiteSpace(item.TestFile) || item.TestFile.Length > UnitTestCase.MaxPathLength) return Lang.T("file test kosong atau terlalu panjang.", "the test file is empty or too long.");
        if (string.IsNullOrWhiteSpace(item.MethodName) || string.IsNullOrWhiteSpace(item.Selector) || item.Selector.Length > UnitTestCase.MaxSelectorLength)
            return Lang.T("nama atau selector test kosong atau terlalu panjang.", "the test name or selector is empty or too long.");
        if (string.IsNullOrWhiteSpace(item.Framework)) return Lang.T("framework kosong.", "empty framework.");
        if (!Enum.IsDefined(item.State) || !Enum.IsDefined(item.Origin) || !Enum.IsDefined(item.LastResult)) return Lang.T("status, asal, atau hasil terakhir tidak dikenal.", "unknown status, origin or last result.");
        return null;
    }
}

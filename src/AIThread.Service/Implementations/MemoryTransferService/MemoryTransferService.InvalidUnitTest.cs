using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? InvalidUnitTest(UnitTestExportItem? item)
    {
        if (item is null) return "kosong.";
        if (item.Id == Guid.Empty) return "id kosong.";
        if (string.IsNullOrWhiteSpace(item.ProjectKey) || item.ProjectKey.Length > 300) return "project key kosong atau lebih dari 300 karakter.";
        if (string.IsNullOrWhiteSpace(item.Title)) return "judul kosong.";
        if (string.IsNullOrWhiteSpace(item.TestFile) || item.TestFile.Length > UnitTestCase.MaxPathLength) return "file test kosong atau terlalu panjang.";
        if (string.IsNullOrWhiteSpace(item.MethodName) || string.IsNullOrWhiteSpace(item.Selector) || item.Selector.Length > UnitTestCase.MaxSelectorLength)
            return "nama atau selector test kosong atau terlalu panjang.";
        if (string.IsNullOrWhiteSpace(item.Framework)) return "framework kosong.";
        if (!Enum.IsDefined(item.State) || !Enum.IsDefined(item.Origin) || !Enum.IsDefined(item.LastResult)) return "status, asal, atau hasil terakhir tidak dikenal.";
        return null;
    }
}

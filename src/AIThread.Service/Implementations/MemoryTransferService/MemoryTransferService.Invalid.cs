using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? Invalid(MemoryExportItem? item)
    {
        if (item is null) return "kosong.";
        if (item.Id == Guid.Empty) return "id kosong.";
        if (item.Level is not (MemoryLevel.Short or MemoryLevel.Long)) return "level harus Short atau Long (business memory tidak termasuk).";
        if (item.Type is not (MemoryType.Project or MemoryType.Knowledge)) return "tipe harus Project atau Knowledge.";
        if (string.IsNullOrWhiteSpace(item.Label) && string.IsNullOrWhiteSpace(item.Module)) return "label kosong.";
        if (item.Label?.Trim().Length > 300) return "label lebih dari 300 karakter.";
        if (string.IsNullOrWhiteSpace(item.Description)) return "deskripsi kosong.";
        if (item.ProjectKey?.Length > 300) return "project key lebih dari 300 karakter.";
        return null;
    }
}

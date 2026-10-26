using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? Invalid(MemoryExportItem? item)
    {
        if (item is null) return Lang.T("kosong.", "empty.");
        if (item.Id == Guid.Empty) return Lang.T("id kosong.", "empty id.");
        if (item.Level is not (MemoryLevel.Short or MemoryLevel.Long)) return Lang.T("level harus Short atau Long (business memory tidak termasuk).", "the level must be Short or Long (business memory is not included).");
        if (item.Type is not (MemoryType.Project or MemoryType.Knowledge)) return Lang.T("tipe harus Project atau Knowledge.", "the type must be Project or Knowledge.");
        if (string.IsNullOrWhiteSpace(item.Label) && string.IsNullOrWhiteSpace(item.Module)) return Lang.T("label kosong.", "empty label.");
        if (item.Label?.Trim().Length > 300) return Lang.T("label lebih dari 300 karakter.", "the label is longer than 300 characters.");
        if (string.IsNullOrWhiteSpace(item.Description)) return Lang.T("deskripsi kosong.", "empty description.");
        if (item.ProjectKey?.Length > 300) return Lang.T("project key lebih dari 300 karakter.", "the project key is longer than 300 characters.");
        return null;
    }
}

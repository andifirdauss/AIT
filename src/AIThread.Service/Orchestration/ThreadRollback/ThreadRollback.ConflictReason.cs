using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private static string? ConflictReason(IReadOnlyList<RollbackFileDto> files)
    {
        var conflicts = files.Where(f => f.Action == Conflict).ToList();
        if (conflicts.Count > 0)
            return Lang.T($"Pembatalan ditolak: {conflicts.Count} file sudah berubah setelah thread ini ({string.Join(", ", conflicts.Select(c => c.FilePath))}). ", $"Rollback refused: {conflicts.Count} file(s) changed after this thread ({string.Join(", ", conflicts.Select(c => c.FilePath))}). ") +
                   Lang.T("Tidak ada file yang disentuh. Kembalikan file itu secara manual (misalnya lewat git) atau batalkan dulu thread yang lebih baru.", "No file was touched. Restore that file by hand (e.g. with git) or roll back the newer thread first.");
        return files.All(f => f.Action == Unchanged) ? Lang.T("Semua file sudah sama dengan sebelum thread ini; tidak ada yang perlu dibatalkan.", "Every file is already the same as before this thread; nothing to roll back.") : null;
    }
}

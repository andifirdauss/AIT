using AiThread.Common.DTOs;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private static string? ConflictReason(IReadOnlyList<RollbackFileDto> files)
    {
        var conflicts = files.Where(f => f.Action == Conflict).ToList();
        if (conflicts.Count > 0)
            return $"Pembatalan ditolak: {conflicts.Count} file sudah berubah setelah thread ini ({string.Join(", ", conflicts.Select(c => c.FilePath))}). " +
                   "Tidak ada file yang disentuh. Kembalikan file itu secara manual (misalnya lewat git) atau batalkan dulu thread yang lebih baru.";
        return files.All(f => f.Action == Unchanged) ? "Semua file sudah sama dengan sebelum thread ini; tidak ada yang perlu dibatalkan." : null;
    }
}

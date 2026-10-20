using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private static string? Refusal(RollbackState state) => state switch
    {
        { Thread.Status: ThreadStatus.RolledBack } => "Perubahan thread ini sudah dibatalkan sebelumnya.",
        { Running: true } or { Thread.Status: ThreadStatus.Pending or ThreadStatus.Planning or ThreadStatus.Executing or ThreadStatus.Verifying or ThreadStatus.Wrapping }
            => "Thread ini sedang berjalan; perubahan baru bisa dibatalkan setelah thread selesai.",
        { Root: null } => "Project thread ini sudah dihapus, jadi lokasi file tidak diketahui.",
        { Snapshots.Count: 0 } => "Thread ini tidak mengubah file apa pun (atau dibuat sebelum fitur pembatalan ada).",
        _ => null
    };
}

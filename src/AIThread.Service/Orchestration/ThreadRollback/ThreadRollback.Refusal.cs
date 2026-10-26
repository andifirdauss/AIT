using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private static string? Refusal(RollbackState state) => state switch
    {
        { Thread.Status: ThreadStatus.RolledBack } => Lang.T("Perubahan thread ini sudah dibatalkan sebelumnya.", "The changes of this thread were already rolled back."),
        { Running: true } or { Thread.Status: ThreadStatus.Pending or ThreadStatus.Planning or ThreadStatus.Executing or ThreadStatus.Verifying or ThreadStatus.Testing or ThreadStatus.Wrapping }
            => Lang.T("Thread ini sedang berjalan; perubahan baru bisa dibatalkan setelah thread selesai.", "This thread is running; its changes can only be rolled back after it finishes."),
        { Root: null } => Lang.T("Project thread ini sudah dihapus, jadi lokasi file tidak diketahui.", "The project of this thread was deleted, so the file locations are unknown."),
        { Snapshots.Count: 0 } => Lang.T("Thread ini tidak mengubah file apa pun (atau dibuat sebelum fitur pembatalan ada).", "This thread changed no files (or was created before rollback existed)."),
        _ => null
    };
}

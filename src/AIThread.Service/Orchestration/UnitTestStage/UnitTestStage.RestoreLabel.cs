using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public static string RestoreLabel(UnitTestRestoreItem item) => item.How switch
    {
        UnitTestRestoreItem.Moved => item.Note ?? "pindah file",
        UnitTestRestoreItem.Snapshot => item.Note is null ? "dipulihkan dari snapshot" : $"{item.Note} dipulihkan dari snapshot",
        UnitTestRestoreItem.Method => "method dikembalikan dari snapshot",
        UnitTestRestoreItem.Task => "dikembalikan executor dari snapshot method",
        UnitTestRestoreItem.Regenerate => "ditulis ulang executor dari skenario (perlu konfirmasi developer)",
        _ => item.Note is null ? "tidak bisa dipulihkan" : $"tidak bisa dipulihkan: {item.Note}"
    };
}

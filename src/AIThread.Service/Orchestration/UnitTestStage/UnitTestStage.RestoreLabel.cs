using AIThread.Common.Localization;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public static string RestoreLabel(UnitTestRestoreItem item) => item.How switch
    {
        UnitTestRestoreItem.Moved => item.Note ?? Lang.T("pindah file", "moved file"),
        UnitTestRestoreItem.Snapshot => item.Note is null ? Lang.T("dipulihkan dari snapshot", "restored from snapshot") : Lang.T($"{item.Note} dipulihkan dari snapshot", $"{item.Note} restored from snapshot"),
        UnitTestRestoreItem.Method => Lang.T("method dikembalikan dari snapshot", "method put back from snapshot"),
        UnitTestRestoreItem.Task => Lang.T("dikembalikan executor dari snapshot method", "put back by an executor from the method snapshot"),
        UnitTestRestoreItem.Regenerate => Lang.T("ditulis ulang executor dari skenario (perlu konfirmasi developer)", "rewritten by an executor from the scenario (needs developer confirmation)"),
        _ => item.Note is null ? Lang.T("tidak bisa dipulihkan", "could not be restored") : Lang.T($"tidak bisa dipulihkan: {item.Note}", $"could not be restored: {item.Note}")
    };
}

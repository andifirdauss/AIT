using AIThread.Common.Enums;
using AIThread.Service.UnitTests;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // Progress: one line per restored test and the git notes. When executor tasks follow, the caller closes the phase.
    public async Task<IReadOnlyList<PlanTask>> RestoreAsync(UnitTestSession session, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        UnitTestRestoreResult result;
        try
        {
            result = await restorer.RestoreAsync(session.Project, session.Tests, session.Suites, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Missing unit tests of {Project} could not be restored.", session.Project.Name);
            recorder.Log(ActivityLevel.Warning, $"Pemulihan unit test gagal: {ex.Message}");
            return [];
        }
        if (result.Items.Count == 0 && result.Notes.Count == 0) return [];

        session.Report.Restored.AddRange(result.Items);
        session.Report.Notes.AddRange(result.Notes);
        await progress.PhaseAsync("🧷", $"Pemulihan unit test · {result.Items.Count} test", cancellationToken);
        foreach (var item in result.Items.Take(MaxRestoreLines))
            await progress.StepAsync($"{RestoreIcon(item.How)} {item.Number} {item.File} — {RestoreLabel(item)}", cancellationToken, hang: 3);
        if (result.Items.Count > MaxRestoreLines) await progress.StepAsync($"… {result.Items.Count - MaxRestoreLines} lainnya", cancellationToken, hang: 3);
        foreach (var note in result.Notes) await progress.StepAsync($"ℹ️ {note}", cancellationToken, hang: 3);
        if (result.Tasks.Count == 0) await progress.StepAsync("selesai", cancellationToken, last: true);

        recorder.Log(ActivityLevel.Info, "Unit test dipulihkan otomatis: " + string.Join("; ", result.Items.Select(i => $"{i.Number} {RestoreLabel(i)}")));
        return result.Tasks;
    }
}

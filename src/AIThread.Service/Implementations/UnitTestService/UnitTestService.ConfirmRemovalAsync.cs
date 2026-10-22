using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<ConfirmUnitTestRemovalResultDto> ConfirmRemovalAsync(Guid projectId, Guid testId, ConfirmUnitTestRemovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var (project, test) = await TestAsync(projectId, testId, cancellationToken);
        if (test.State != UnitTestState.Obsolete)
            throw new AppValidationException($"{test.DisplayNumber} belum ditandai obsolete; hanya test obsolete yang bisa dihapus dari katalog.");

        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        test.State = UnitTestState.Removed;
        test.RemovedBy = by;
        test.RemovedAt = DateTime.UtcNow;
        UnitTestSnapshot.Clear(test);
        await catalog.UpdateAsync([test], cancellationToken);
        activityLog.Info("unittest", $"{test.DisplayNumber} {test.Title} dihapus dari katalog oleh {by}.", projectId: projectId);

        Guid? threadId = null;
        if (request.RemoveCode && File.Exists(Path.Combine(project.RootPath, test.TestFile)))
        {
            var prompt = $"Hapus unit test {test.DisplayNumber} \"{test.Title}\" dari kode: `{test.MethodName}`"
                         + (test.ClassName is null ? string.Empty : $" di class `{test.ClassName}`") + $", file `{test.TestFile}`. "
                         + $"Test ini sudah dikonfirmasi obsolete di katalog unit test (alasan: {test.ObsoleteReason ?? "tidak dicatat"}). "
                         + "Hapus hanya test itu; kalau file-nya tidak berisi test lain lagi, hapus file-nya. Jangan ubah kode lain.";
            threadId = await orchestrator.StartInBackgroundAsync(
                new ThreadRequest(ThreadSource.Dashboard, prompt, prompt, null, project.RootPath, project.ProjectId, null), cancellationToken);
        }
        return new ConfirmUnitTestRemovalResultDto(UnitTestCatalog.ToDto(test), threadId);
    }
}

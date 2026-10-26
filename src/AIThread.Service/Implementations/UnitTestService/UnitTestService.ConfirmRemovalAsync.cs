using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
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
            throw new AppValidationException(Lang.T($"{test.DisplayNumber} belum ditandai obsolete; hanya test obsolete yang bisa dihapus dari katalog.", $"{test.DisplayNumber} is not marked obsolete yet; only obsolete tests can be removed from the catalog."));

        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        test.State = UnitTestState.Removed;
        test.RemovedBy = by;
        test.RemovedAt = DateTime.UtcNow;
        UnitTestSnapshot.Clear(test);
        await catalog.UpdateAsync([test], cancellationToken);
        activityLog.Info("unittest", Lang.T($"{test.DisplayNumber} {test.Title} dihapus dari katalog oleh {by}.", $"{test.DisplayNumber} {test.Title} removed from the catalog by {by}."), projectId: projectId);

        Guid? threadId = null;
        if (request.RemoveCode && File.Exists(Path.Combine(project.RootPath, test.TestFile)))
        {
            var prompt = Lang.T($"Hapus unit test {test.DisplayNumber} \"{test.Title}\" dari kode: `{test.MethodName}`", $"Remove unit test {test.DisplayNumber} \"{test.Title}\" from the code: `{test.MethodName}`")
                         + (test.ClassName is null ? string.Empty : Lang.T($" di class `{test.ClassName}`", $" in class `{test.ClassName}`")) + $", file `{test.TestFile}`. "
                         + Lang.T($"Test ini sudah dikonfirmasi obsolete di katalog unit test (alasan: {test.ObsoleteReason ?? "tidak dicatat"}). ", $"This test is already confirmed obsolete in the unit test catalog (reason: {test.ObsoleteReason ?? "not recorded"}). ")
                         + Lang.T("Hapus hanya test itu; kalau file-nya tidak berisi test lain lagi, hapus file-nya. Jangan ubah kode lain.", "Remove only that test; if its file holds no other test anymore, delete the file. Do not change other code.");
            threadId = await orchestrator.StartInBackgroundAsync(
                new ThreadRequest(ThreadSource.Dashboard, prompt, prompt, null, project.RootPath, project.ProjectId, null), cancellationToken);
        }
        return new ConfirmUnitTestRemovalResultDto(UnitTestCatalog.ToDto(test), threadId);
    }
}

using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // After every label: the files of a registered project that have no entry yet, at most once per process (see
    // _scannedKeys). Progress goes to the status detail, e.g. "KN2018_KCO (1/12) · 363/1.700 file".
    private async Task ScanProjectAsync(RegisteredProject project, string label, CancellationToken cancellationToken)
    {
        if (_scannedKeys.Contains(project.Key)) return;
        SetPhase(Lang.T("Peta kode lengkap", "Full code map"), label);
        try
        {
            var context = await projectService.GetContextAsync(project.Id, cancellationToken);
            if (context is null) return;
            var (files, mapped) = await codeMapper.ScanAsync(context, ScanMaxFiles, cancellationToken, (done, total) => ReportFiles(label, done, total));
            _scannedKeys.Add(project.Key);
            if (files > 0) activityLog.Info("memory", Lang.T($"Peta kode {project.Name}: {mapped} dari {files} file dipetakan ke modul.", $"Code map {project.Name}: {mapped} of {files} files mapped to modules."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map scan of {Project} failed.", project.Name);
        }
    }
}

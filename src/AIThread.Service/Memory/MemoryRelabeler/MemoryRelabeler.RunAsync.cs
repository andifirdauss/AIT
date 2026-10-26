using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // One full pass. modulesOnly: only the duplicate check of modules created since the last pass.
    public async Task RunAsync(bool modulesOnly, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)) return;
        try
        {
            Publish("relabel");
            var relabeledKeys = new HashSet<string>(StringComparer.Ordinal);
            if (!modulesOnly)
            {
                var projects = await ProjectsAsync(cancellationToken);
                var pendingByKey = await PendingByKeyAsync(projects, cancellationToken);
                var pending = pendingByKey.Values.Sum(d => d.Count);
                if (pending > 0)
                {
                    SetPhase("Backup");
                    if (!await BackupAsync(cancellationToken)) return;
                }

                SetPhase(Lang.T("Wiki bisnis", "Business wiki"));
                await NormalizeWikiAsync(cancellationToken);

                if (pending > 0)
                {
                    var registered = projects
                        .Where(p => !string.IsNullOrEmpty(p.Value.Key))
                        .DistinctBy(p => p.Value.Key)
                        .Select(p => new RegisteredProject(p.Key, p.Value.Key!, p.Value.Name))
                        .ToList();
                    var (labeled, reviewed) = await MigrateAsync(projects, registered, pendingByKey, relabeledKeys, cancellationToken);
                    if (labeled + reviewed > 0)
                    {
                        _lastMessage = Lang.T($"{labeled} memory dilabeli ulang, {reviewed} perlu review.", $"{labeled} memories relabeled, {reviewed} need review.");
                        activityLog.Info("memory", Lang.T($"Migrasi label memory: {_lastMessage}", $"Memory label migration: {_lastMessage}"));
                    }

                    // The full code map only helps later threads; it comes after every label, projects with memory first.
                    var scans = registered.Where(p => !_scannedKeys.Contains(p.Key))
                        .OrderByDescending(p => pendingByKey.GetValueOrDefault(p.Key)?.Count ?? 0)
                        .ToList();
                    for (var i = 0; i < scans.Count; i++)
                        await ScanProjectAsync(scans[i], $"{scans[i].Name} ({i + 1}/{scans.Count})", cancellationToken);
                }
            }

            SetPhase(Lang.T("Cek modul ganda", "Duplicate module check"));
            await CheckModulesAsync(relabeledKeys, cancellationToken);
            _lastRunAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastMessage = Lang.T($"Migrasi label berhenti: {ex.Message}", $"Label migration stopped: {ex.Message}");
            logger.LogWarning(ex, "Memory relabeling stopped.");
        }
        finally
        {
            _phase = null;
            _detail = null;
            _gate.Release();
            Publish("relabel");
        }
    }
}

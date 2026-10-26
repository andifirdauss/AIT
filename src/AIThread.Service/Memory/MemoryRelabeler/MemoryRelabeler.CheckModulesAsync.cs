using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // F4: a full duplicate check of the keys whose memory was just relabeled (with the duplicate memory report), and
    // for every other key only the modules created since the last pass. Nothing is re-checked on a plain restart.
    private async Task CheckModulesAsync(IReadOnlySet<string> relabeledKeys, CancellationToken cancellationToken)
    {
        var created = registry.DrainCreated();
        var keys = new HashSet<string>(created.Keys, StringComparer.Ordinal);
        keys.UnionWith(relabeledKeys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (relabeledKeys.Contains(key))
                {
                    var found = await merger.CheckAsync(key, null, cancellationToken);
                    var duplicates = await CountDuplicateMemoriesAsync(key, cancellationToken);
                    if (found > 0 || duplicates > 0)
                        activityLog.Info("memory", Lang.T($"Cek modul {key}: {found} modul ganda ditangani, {duplicates} pasang kandidat memory duplikat (lihat Memori → Perlu review).", $"Module check {key}: {found} duplicate modules handled, {duplicates} pairs of possible duplicate memories (see Memory → Needs review)."));
                }
                else if (created.TryGetValue(key, out var modules))
                {
                    await merger.CheckAsync(key, modules, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Module check of {Key} failed.", key);
            }
        }
    }
}

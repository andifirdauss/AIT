using AIThread.Common.Localization;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // F3 for one registry key (null: every memory still waiting), within the batch budget of the run. Returns the
    // counts; the keys whose memory changed are added to relabeledKeys.
    private async Task<(int Labeled, int Reviewed)> RelabelAsync(IReadOnlyDictionary<Guid, (string? Key, string Name)> projects, string? onlyKey,
        ISet<string> relabeledKeys, CancellationToken cancellationToken)
    {
        if (!await ai.IsAvailableAsync(cancellationToken))
        {
            _lastMessage = Lang.T("Migrasi label menunggu: belum ada node Tools node, Wrapper, atau Planner yang aktif.", "Label migration waiting: there is no active Tools node, Wrapper or Planner yet.");
            return (0, 0);
        }

        int labeled = 0, reviewed = 0;
        while (_batchesLeft > 0)
        {
            var candidates = await ClaimAsync(projects, onlyKey, cancellationToken);
            if (candidates.Count == 0) break;
            _batchesLeft--;

            foreach (var group in candidates.GroupBy(c => c.Key))
            {
                var (l, r) = await LabelGroupAsync(group.Key, group.ToList(), projects, cancellationToken);
                labeled += l;
                reviewed += r;
                if (group.Key is { } key) relabeledKeys.Add(key);
            }
            Publish("relabel");
        }
        return (labeled, reviewed);
    }
}

using AIThread.Common.Entities;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // The oldest entries, at most MaxChangesPerCall, never splitting entries with the same PromotedAt: the watermark
    // moves to the last one folded, so an entry with that same time left for later would be missed.
    private static List<LongMemory> Batch(List<LongMemory> due)
    {
        if (due.Count <= MaxChangesPerCall) return due;
        var next = due[MaxChangesPerCall].PromotedAt;
        var batch = due.Take(MaxChangesPerCall).Where(m => m.PromotedAt < next).ToList();
        return batch.Count > 0 ? batch : due.Where(m => m.PromotedAt <= next).ToList();
    }
}

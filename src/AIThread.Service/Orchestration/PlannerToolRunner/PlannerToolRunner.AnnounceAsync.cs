using System.Text.Json.Nodes;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The progress line of a tool call; run_build writes its own lines (the command it resolved, then the result).
    public static async Task AnnounceAsync(string tool, JsonObject args, string actor, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken, string? badge = null)
    {
        var detail = Describe(tool, args);
        recorder.PlannerActivity(tool, prefix + detail);
        recorder.Log(ActivityLevel.Debug, $"{actor} → {tool} {detail}");
        if (tool != PlannerTools.RunBuild)
            await progress.StepAsync(Lead(prefix, tool) + ClientProgress.BadgeSlot(badge) + Describe(tool, args, compact: true), cancellationToken, hang: LeadColumn + ClientProgress.SlotWidth);
    }
}

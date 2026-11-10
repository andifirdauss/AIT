using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

// One node of the benchmark and how many model calls a small change makes to it.
public sealed record BenchmarkStep(AiNode Node, int Calls);

// Times: the samples that answered, in milliseconds. Error: why the last failed sample failed.
public sealed record ModelMeasurement(string Model, IReadOnlyList<long> Times, int Failed, string? Error);

// From the measured models to an estimate of one small change, its slowest node and what to do about it.
public static class NodeBenchmarkReport
{
    // Measured on real one-constant changes: the planner reads and submits, a tools node takes about three steps.
    public const int PlannerCalls = 2;
    public const int ToolsNodeCalls = 3;

    // A median below this is not worth a tip.
    private const long SlowMs = 3_000;

    // A swing smaller than this is ordinary network noise.
    private const long UnstableSwingMs = 5_000;

    // How much faster another model must be to be worth naming.
    private const int WorthSwitchingFactor = 2;

    // The wrapper is called only when small changes do not skip it (AIThreadOptions.WrapperSkipMaxTasks).
    public static IReadOnlyList<BenchmarkStep> Steps(AiNode? planner, AiNode? toolsNode, AiNode? executor, AiNode? reviewer, AiNode? wrapper, int wrapperSkipMaxTasks)
    {
        var steps = new List<BenchmarkStep>();
        if (planner is not null) steps.Add(new BenchmarkStep(planner, PlannerCalls));
        if (toolsNode is not null) steps.Add(new BenchmarkStep(toolsNode, ToolsNodeCalls));
        if (executor is not null) steps.Add(new BenchmarkStep(executor, 1));
        if (reviewer is not null) steps.Add(new BenchmarkStep(reviewer, 1));
        if (wrapper is not null && wrapperSkipMaxTasks <= 0) steps.Add(new BenchmarkStep(wrapper, 1));
        return steps;
    }

    public static NodeBenchmarkDto Build(IReadOnlyList<BenchmarkStep> steps, IReadOnlyList<ModelMeasurement> measured, int samplesPerModel)
    {
        var byModel = measured.ToDictionary(m => m.Model, StringComparer.Ordinal);
        var rows = steps.Select(step =>
        {
            var measure = byModel[step.Node.Model];
            var median = Median(measure.Times);
            return (Step: step, Measure: measure, Median: median, Estimated: median * step.Calls);
        }).ToList();

        var total = rows.Sum(r => r.Estimated ?? 0);
        var slowest = rows.Where(r => r.Estimated is not null).OrderByDescending(r => r.Estimated).Select(r => r.Step).FirstOrDefault();
        var stages = rows.Select(r => new NodeBenchmarkStageDto(
            r.Step.Node.Role,
            r.Step.Node.Id,
            r.Step.Node.Name,
            r.Step.Node.Model,
            r.Step.Calls,
            r.Median,
            r.Measure.Times.Count == 0 ? null : r.Measure.Times.Min(),
            r.Measure.Times.Count == 0 ? null : r.Measure.Times.Max(),
            r.Estimated,
            total > 0 && r.Estimated is { } estimated ? (int)Math.Round(estimated * 100.0 / total) : 0,
            r.Measure.Failed,
            r.Measure.Error,
            IsUnstable(r.Measure.Times, r.Median),
            ReferenceEquals(r.Step, slowest))).ToList();

        return new NodeBenchmarkDto(stages, total, samplesPerModel, slowest?.Node.Name, Tips(stages, measured));
    }

    private static long? Median(IReadOnlyList<long> times) => times.Count == 0 ? null : times.Order().ElementAt(times.Count / 2);

    // A model that takes more than twice its usual time now and then, by a margin a developer would notice.
    private static bool IsUnstable(IReadOnlyList<long> times, long? median) =>
        median is { } usual && times.Count >= 2 && times.Max() >= usual * 2 && times.Max() - usual >= UnstableSwingMs;

    private static string Seconds(long ms) => $"{ms / 1000.0:0.0}";

    private static List<string> Tips(IReadOnlyList<NodeBenchmarkStageDto> stages, IReadOnlyList<ModelMeasurement> measured)
    {
        if (stages.Count == 0)
            return [Lang.T("Belum ada node aktif untuk diukur. Tambahkan node Planner dan Executor dulu.", "There is no active node to measure yet. Add a Planner and an Executor node first.")];

        var tips = new List<string>();
        foreach (var stage in stages.Where(s => s.Failed > 0))
            tips.Add(Lang.T($"{stage.NodeName} gagal menjawab {stage.Failed} kali ({stage.Error}). Periksa model {stage.Model} dan kuotanya, atau pasang node cadangan.",
                $"{stage.NodeName} failed to answer {stage.Failed} time(s) ({stage.Error}). Check the model {stage.Model} and its quota, or add a fallback node."));

        if (stages.FirstOrDefault(s => s.IsBottleneck) is { MedianMs: >= SlowMs } slowest) tips.Add(BottleneckTip(slowest, measured));

        foreach (var model in stages.Where(s => s.Unstable).GroupBy(s => s.Model, StringComparer.Ordinal).Select(g => g.First()))
            tips.Add(Lang.T($"Waktu {model.Model} naik turun ({Seconds(model.MinMs!.Value)}–{Seconds(model.MaxMs!.Value)} detik), biasanya karena provider sedang padat atau kena rate limit. Pasang node cadangan atau pindah ke model yang lebih stabil.",
                $"The time of {model.Model} swings ({Seconds(model.MinMs!.Value)}–{Seconds(model.MaxMs!.Value)} seconds), usually because the provider is busy or rate limiting. Add a fallback node or move to a steadier model."));

        if (stages.Any(s => s.Role == NodeRole.ToolsNode))
            tips.Add(Lang.T("Nyalakan Planner baca langsung di node Planner, supaya perubahan kecil tidak perlu menunggu tools node.",
                "Turn on Planner reads directly in the Planner node, so a small change does not wait for the tools node."));

        if (stages.Any(s => s.Role == NodeRole.Wrapper))
            tips.Add(Lang.T("Isi AIThread:WrapperSkipMaxTasks di appsettings.json agar perubahan kecil tidak memanggil Wrapper.",
                "Set AIThread:WrapperSkipMaxTasks in appsettings.json so a small change does not call the Wrapper."));

        if (tips.Count == 0)
            tips.Add(Lang.T("Tidak ada titik lambat yang berarti: semua model menjawab cepat dan stabil.", "No slow spot worth fixing: every model answers quickly and steadily."));
        return tips;
    }

    // The planner is meant to be the strongest model: a faster one is a trade-off, not a free win.
    private static string BottleneckTip(NodeBenchmarkStageDto slowest, IReadOnlyList<ModelMeasurement> measured)
    {
        var median = Seconds(slowest.MedianMs!.Value);
        if (slowest.Role == NodeRole.Planner)
            return Lang.T($"{slowest.NodeName} memakan {slowest.SharePercent}% waktu ({median} detik per panggilan). Wajar untuk model terkuat; model yang lebih ringan akan lebih cepat, dengan risiko rencananya kurang teliti.",
                $"{slowest.NodeName} takes {slowest.SharePercent}% of the time ({median} seconds per call). That is normal for the strongest model; a lighter model is faster, at the risk of a less careful plan.");

        var faster = measured
            .Where(m => m.Times.Count > 0 && !string.Equals(m.Model, slowest.Model, StringComparison.Ordinal))
            .Select(m => (m.Model, Median: Median(m.Times)!.Value))
            .Where(m => m.Median * WorthSwitchingFactor <= slowest.MedianMs!.Value)
            .OrderBy(m => m.Median)
            .FirstOrDefault();
        var hint = faster.Model is null
            ? Lang.T("Coba model yang lebih cepat untuk node ini.", "Try a faster model for this node.")
            : Lang.T($"Di pengukuran ini {faster.Model} jauh lebih cepat ({Seconds(faster.Median)} detik).", $"In this measurement {faster.Model} is much faster ({Seconds(faster.Median)} seconds).");
        return Lang.T($"{slowest.NodeName} adalah titik lambat: {slowest.SharePercent}% waktu, {median} detik per panggilan dengan {slowest.Model}. {hint}",
            $"{slowest.NodeName} is the slow spot: {slowest.SharePercent}% of the time, {median} seconds per call with {slowest.Model}. {hint}");
    }
}

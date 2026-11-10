using AIThread.Service.Orchestration;

namespace AIThread.Service.Implementations;

public sealed partial class NodeBenchmarkService
{
    // One context size after the other, for the same reason the samples of one size run one by one (see MeasureAsync).
    private async Task<IReadOnlyList<ModelMeasurement>> MeasureModelAsync(string model, IEnumerable<int> contextSizes, CancellationToken cancellationToken)
    {
        var measurements = new List<ModelMeasurement>();
        foreach (var contextTokens in contextSizes) measurements.Add(await MeasureAsync(model, contextTokens, cancellationToken));
        return measurements;
    }
}

using System.Diagnostics;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Implementations;

public sealed partial class NodeBenchmarkService
{
    // One sample after the other: parallel calls to one model would measure its rate limit instead of its speed.
    private async Task<ModelMeasurement> MeasureAsync(string model, int contextTokens, CancellationToken cancellationToken)
    {
        var times = new List<long>();
        var failed = 0;
        string? error = null;
        for (var i = 0; i < Samples; i++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(CallLimit);
            var clock = Stopwatch.StartNew();
            try
            {
                await router.ChatAsync(new ChatCompletionRequest
                {
                    Model = model,
                    Messages = [ChatMessages.User(Probe(contextTokens))],
                    MaxTokens = MaxReplyTokens
                }, cancellationToken: limit.Token);
                times.Add(clock.ElapsedMilliseconds);
            }
            catch (NineRouterException ex)
            {
                failed++;
                error = ex.Message;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                error = Lang.T($"tidak menjawab dalam {CallLimit.TotalSeconds:0} detik", $"no answer within {CallLimit.TotalSeconds:0} seconds");
            }
        }
        return new ModelMeasurement(model, contextTokens, times, failed, error);
    }
}

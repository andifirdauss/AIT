using System.Threading.Channels;
using AiThread.Service.Interfaces;

namespace AiThread.Service.CodeCalls;

// Path + lines: the planner read those lines of a file (relative path). ThreadId: a thread finished; the lines it
// changed in each file are scanned (Root is looked up then).
public sealed record FunctionScanRequest(Guid ProjectId, string Root, string? Path = null, int From = 0, int To = 0, Guid? ThreadId = null);

// Work for FunctionCallWorker. Producers never wait and never fail: the planner and the executors go on whatever
// happens to the scan.
public sealed partial class FunctionCallQueue : IFunctionCallQueue
{
    private readonly Channel<FunctionScanRequest> _channel = Channel.CreateUnbounded<FunctionScanRequest>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<FunctionScanRequest> Reader => _channel.Reader;
}

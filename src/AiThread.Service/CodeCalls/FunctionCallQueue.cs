using System.Threading.Channels;

namespace AiThread.Service.CodeCalls;

// Path + lines: the planner read those lines of a file (relative path). ThreadId: a thread finished; the lines it
// changed in each file are scanned (Root is looked up then).
public sealed record FunctionScanRequest(Guid ProjectId, string Root, string? Path = null, int From = 0, int To = 0, Guid? ThreadId = null);

// Work for FunctionCallWorker. Producers never wait and never fail: the planner and the executors go on whatever
// happens to the scan.
public sealed class FunctionCallQueue
{
    private readonly Channel<FunctionScanRequest> _channel = Channel.CreateUnbounded<FunctionScanRequest>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<FunctionScanRequest> Reader => _channel.Reader;

    public void Read(Guid projectId, string root, string path, int from, int to) =>
        _channel.Writer.TryWrite(new FunctionScanRequest(projectId, root, path.Replace('\\', '/'), Math.Max(1, from), Math.Max(from, to)));

    public void ThreadChanged(Guid projectId, Guid threadId) =>
        _channel.Writer.TryWrite(new FunctionScanRequest(projectId, string.Empty, ThreadId: threadId));
}

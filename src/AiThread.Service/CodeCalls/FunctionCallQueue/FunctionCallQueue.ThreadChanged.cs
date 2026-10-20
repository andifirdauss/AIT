namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallQueue
{
    public void ThreadChanged(Guid projectId, Guid threadId) =>
        _channel.Writer.TryWrite(new FunctionScanRequest(projectId, string.Empty, ThreadId: threadId));
}

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallQueue
{
    public void Read(Guid projectId, string root, string path, int from, int to) =>
        _channel.Writer.TryWrite(new FunctionScanRequest(projectId, root, path.Replace('\\', '/'), Math.Max(1, from), Math.Max(from, to)));
}

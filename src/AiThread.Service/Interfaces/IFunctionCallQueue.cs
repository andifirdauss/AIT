using System.Threading.Channels;
using AiThread.Service.CodeCalls;

namespace AiThread.Service.Interfaces;

public interface IFunctionCallQueue
{
    ChannelReader<FunctionScanRequest> Reader { get; }

    void Read(Guid projectId, string root, string path, int from, int to);

    void ThreadChanged(Guid projectId, Guid threadId);
}

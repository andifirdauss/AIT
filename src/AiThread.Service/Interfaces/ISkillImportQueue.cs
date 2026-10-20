using System.Threading.Channels;

namespace AiThread.Service.Interfaces;

public interface ISkillImportQueue
{
    void Enqueue(Guid sourceId);

    ChannelReader<Guid> Reader { get; }
}

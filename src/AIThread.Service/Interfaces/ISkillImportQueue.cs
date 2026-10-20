using System.Threading.Channels;

namespace AIThread.Service.Interfaces;

public interface ISkillImportQueue
{
    void Enqueue(Guid sourceId);

    ChannelReader<Guid> Reader { get; }
}

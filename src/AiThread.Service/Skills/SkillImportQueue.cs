using System.Threading.Channels;

namespace AiThread.Service.Skills;

// Skill sources waiting to be pulled; SkillImportWorker pulls them one at a time.
public sealed class SkillImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid sourceId) => _channel.Writer.TryWrite(sourceId);

    public ChannelReader<Guid> Reader => _channel.Reader;
}

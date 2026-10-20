using System.Threading.Channels;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Skills;

// Skill sources waiting to be pulled; SkillImportWorker pulls them one at a time.
public sealed partial class SkillImportQueue : ISkillImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;
}

using System.Threading.Channels;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Skills;

// Skill sources waiting to be pulled; SkillImportWorker pulls them one at a time.
public sealed partial class SkillImportQueue : ISkillImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;
}

using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// Pools every executor result and turns it into one reply for the developer.
public sealed partial class WrapperAgent(INodeCaller caller, INodePromptBuilder prompts) : IWrapperAgent
{
    private const int MaxDiffLinesPerTask = 60;
}

using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class NodeConcurrencyLimiterTests
{
    [Fact]
    public async Task Calls_beyond_max_concurrency_wait_for_a_free_slot()
    {
        var limiter = new NodeConcurrencyLimiter();
        var planner = new AiNode { Role = NodeRole.Planner, Model = "m", MaxConcurrency = 2 };

        var first = await limiter.AcquireAsync(planner, CancellationToken.None);
        var second = await limiter.AcquireAsync(planner, CancellationToken.None);
        var third = limiter.AcquireAsync(planner, CancellationToken.None);

        await Task.Delay(50);
        Assert.False(third.IsCompleted);
        Assert.Equal(0, limiter.Available(planner));

        first.Dispose();
        using var granted = await third.WaitAsync(TimeSpan.FromSeconds(1));
        second.Dispose();
        Assert.Equal(1, limiter.Available(planner));
    }

    [Fact]
    public async Task Nodes_have_independent_limits_and_releasing_twice_is_harmless()
    {
        var limiter = new NodeConcurrencyLimiter();
        var planner = new AiNode { Role = NodeRole.Planner, Model = "a", MaxConcurrency = 1 };
        var wrapper = new AiNode { Role = NodeRole.Wrapper, Model = "b", MaxConcurrency = 1 };

        var lease = await limiter.AcquireAsync(planner, CancellationToken.None);
        using (await limiter.AcquireAsync(wrapper, CancellationToken.None))
            Assert.Equal(0, limiter.Available(wrapper));

        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, limiter.Available(planner));
        Assert.Equal(1, limiter.Available(wrapper));
    }
}

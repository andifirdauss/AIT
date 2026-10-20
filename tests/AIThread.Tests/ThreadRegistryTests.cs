using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class ThreadRegistryTests
{
    [Fact]
    public async Task CancelAll_cancels_every_running_thread_and_waits_until_they_complete()
    {
        var registry = new ThreadRegistry();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstToken = registry.Register(first, CancellationToken.None).Token;
        var secondToken = registry.Register(second, CancellationToken.None).Token;

        Assert.Equal(2, registry.CancelAll());
        Assert.True(firstToken.IsCancellationRequested);
        Assert.True(secondToken.IsCancellationRequested);

        var idle = registry.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(idle.IsCompleted);

        registry.Complete(first);
        registry.Complete(second);
        Assert.True(await idle);
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task WaitForIdle_gives_up_after_the_timeout()
    {
        var registry = new ThreadRegistry();
        registry.Register(Guid.NewGuid(), CancellationToken.None);

        Assert.False(await registry.WaitForIdleAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None));
        Assert.Equal(1, registry.ActiveCount);
    }

    [Fact]
    public void Cancel_after_complete_reports_not_running()
    {
        var registry = new ThreadRegistry();
        var id = Guid.NewGuid();
        registry.Register(id, CancellationToken.None);
        registry.Complete(id);

        Assert.False(registry.Cancel(id));
        Assert.Equal(0, registry.CancelAll());
    }
}

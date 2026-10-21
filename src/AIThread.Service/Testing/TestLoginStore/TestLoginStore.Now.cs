namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}

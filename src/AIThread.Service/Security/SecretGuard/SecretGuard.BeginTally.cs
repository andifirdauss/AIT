namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Counts the distinct secrets masked by everything that runs inside it (async calls and tasks started from it
    // included) until it is disposed; TallyCount reads it.
    public IDisposable BeginTally() => new Tally(_tally);
}

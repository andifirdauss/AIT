namespace AIThread.Service.Interfaces;

public interface ISessionHandoff
{
    Task BeginAsync(string sessionId, string transcriptPath, string workingDirectory, CancellationToken cancellationToken);

    Task<string?> TakeAsync(string newSessionId, string workingDirectory, CancellationToken cancellationToken);
}

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestRunner
{
    public bool IsRunning(string projectKey) => _running.ContainsKey(projectKey);
}

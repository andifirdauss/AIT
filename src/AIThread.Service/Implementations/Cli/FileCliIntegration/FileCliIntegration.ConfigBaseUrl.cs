namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    // The URL the file holds for this proxy; CLIs that append "/messages" or "/responses" themselves want the /v1 root.
    protected virtual string ConfigBaseUrl(string proxyBaseUrl) => proxyBaseUrl.Trim().TrimEnd('/');
}

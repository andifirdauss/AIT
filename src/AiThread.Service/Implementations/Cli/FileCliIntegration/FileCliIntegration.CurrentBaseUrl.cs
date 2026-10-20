namespace AiThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    // The base URL of AiThread's entry in the file, or null when it has none.
    protected abstract string? CurrentBaseUrl(string content);
}

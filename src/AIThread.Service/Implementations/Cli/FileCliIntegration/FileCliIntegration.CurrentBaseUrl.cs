namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    // The base URL of AIThread's entry in the file, or null when it has none.
    protected abstract string? CurrentBaseUrl(string content);
}

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    // The file with AIThread's entry added or replaced; throws AppValidationException when that is unsafe.
    protected abstract string Apply(string content, CliEntry entry);
}

using AIThread.Service.CodeCalls;

namespace AIThread.Service.Interfaces;

public interface IFunctionCallScanner
{
    Task ScanAsync(FunctionScanRequest request, CancellationToken cancellationToken);

    Task ScanFileAsync(Guid projectId, string root, string path, IReadOnlyList<(int From, int To)> ranges, CancellationToken cancellationToken);
}

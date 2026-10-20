namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    public async Task ScanAsync(FunctionScanRequest request, CancellationToken cancellationToken)
    {
        if (request.ThreadId is { } threadId)
        {
            if (await projectService.GetContextAsync(request.ProjectId, cancellationToken) is not { } project) return;
            foreach (var (path, from, to) in await ThreadChangesAsync(project.RootPath, threadId, cancellationToken))
                await ScanFileAsync(request.ProjectId, project.RootPath, path, [(from, to)], cancellationToken);
            return;
        }
        if (request.Path is not null) await ScanFileAsync(request.ProjectId, request.Root, request.Path, [(request.From, request.To)], cancellationToken);
    }
}

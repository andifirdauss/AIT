namespace AiThread.Service.Models;

// Where a thread operates: always a registered project identified by its root folder. Stacks and modules are read
// from the manifests in that folder when the context is built.
public sealed record ProjectContext(
    Guid ProjectId,
    string Name,
    string RootPath,
    IReadOnlyList<string> Stacks,
    IReadOnlyList<string> Modules);

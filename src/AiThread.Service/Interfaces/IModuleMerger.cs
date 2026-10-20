using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;

namespace AiThread.Service.Interfaces;

public interface IModuleMerger
{
    Task<ModuleMergeLog> MergeAsync(string key, string from, string into, string? reason, double? confidence, string decidedBy, CancellationToken cancellationToken, Guid? suggestionId = null);

    Task<string> RenameAsync(string key, string name, string newName, string decidedBy, CancellationToken cancellationToken);

    Task UndoAsync(Guid logId, string decidedBy, CancellationToken cancellationToken);

    Task RejectAsync(Guid logId, string decidedBy, CancellationToken cancellationToken);

    Task AcceptAsync(Guid logId, string decidedBy, CancellationToken cancellationToken);

    Task<int> CheckAsync(string key, IReadOnlyCollection<string>? focus, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModuleMergeDto>> ListAsync(string key, ModuleMergeStatus? status, int take, CancellationToken cancellationToken);
}

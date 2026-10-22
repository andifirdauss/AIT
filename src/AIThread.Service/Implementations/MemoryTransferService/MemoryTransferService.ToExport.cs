using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static UnitTestExportItem ToExport(UnitTestCase t, bool withSnapshot) => new(
        t.Id, t.ProjectKey, t.ProjectName, t.Number, t.Title, t.Purpose, t.Scenario, t.Module, t.Layers, t.Keywords, t.TargetFiles, t.TargetSymbols,
        t.Framework, t.TestProject, t.TestFile, t.ClassName, t.MethodName, t.Selector, t.Requires, t.State, t.Origin, t.CreatedBy, t.CreatedThreadId,
        t.CreatedAt, t.UpdatedAt, t.ObsoleteReason, t.ObsoleteThreadId, t.ObsoleteBy, t.ObsoleteAt, t.QuarantineReason, t.RemovedBy, t.RemovedAt,
        t.LastResult, t.LastRunAt, t.LastRunBy, t.LastDurationMs, t.LastMessage, t.LastSkipReason, t.LastInputHash,
        withSnapshot ? t.SourceSnapshot : null, withSnapshot ? t.SourceHash : null, t.SourceCommit, t.SourceBranch,
        withSnapshot ? t.MethodSnapshot : null, withSnapshot ? t.SupportFiles : null);
}

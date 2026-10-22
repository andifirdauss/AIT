using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // Everything but the id and the number. A test removed here stays removed (it must not come back through a file
    // exported before the removal); a snapshot only comes in when the project keeps snapshots.
    private static void ApplyImported(UnitTestCase test, UnitTestExportItem item, bool keepSnapshot)
    {
        var removedHere = test.State == UnitTestState.Removed;
        test.ProjectKey = item.ProjectKey.Trim();
        test.ProjectName = Trim(item.ProjectName, 200);
        test.Title = Trim(item.Title, 300) ?? test.Title;
        test.Purpose = Trim(item.Purpose, 2_000);
        test.Scenario = Trim(item.Scenario, 4_000);
        test.Module = Trim(item.Module, 100);
        test.Layers = Trim(item.Layers, 100);
        test.Keywords = Trim(item.Keywords, 1_000);
        test.TargetFiles = Trim(item.TargetFiles, 4_000);
        test.TargetSymbols = Trim(item.TargetSymbols, 1_000);
        test.Framework = item.Framework.Trim();
        test.TestProject = Trim(item.TestProject, UnitTestCase.MaxPathLength);
        test.TestFile = item.TestFile.Trim();
        test.ClassName = Trim(item.ClassName, 300);
        test.MethodName = item.MethodName.Trim();
        test.Selector = item.Selector.Trim();
        test.Requires = Trim(item.Requires, 100);
        test.State = removedHere ? UnitTestState.Removed : item.State;
        test.Origin = item.Origin;
        test.CreatedBy = Trim(item.CreatedBy, 200);
        test.CreatedThreadId = item.CreatedThreadId;
        test.CreatedAt = Utc(item.CreatedAt);
        test.UpdatedAt = item.UpdatedAt is { } updated ? Utc(updated) : null;
        test.ObsoleteReason = Trim(item.ObsoleteReason, 1_000);
        test.ObsoleteThreadId = item.ObsoleteThreadId;
        test.ObsoleteBy = Trim(item.ObsoleteBy, 200);
        test.ObsoleteAt = item.ObsoleteAt is { } obsolete ? Utc(obsolete) : null;
        test.QuarantineReason = Trim(item.QuarantineReason, 1_000);
        if (!removedHere)
        {
            test.RemovedBy = Trim(item.RemovedBy, 200);
            test.RemovedAt = item.RemovedAt is { } removed ? Utc(removed) : null;
        }
        test.LastResult = item.LastResult;
        test.LastRunAt = item.LastRunAt is { } run ? Utc(run) : null;
        test.LastRunBy = Trim(item.LastRunBy, 200);
        test.LastDurationMs = item.LastDurationMs;
        test.LastMessage = Trim(item.LastMessage, 2_000);
        test.LastSkipReason = Trim(item.LastSkipReason, 500);
        test.LastInputHash = Trim(item.LastInputHash, 64);
        test.SourceCommit = Trim(item.SourceCommit, 64);
        test.SourceBranch = Trim(item.SourceBranch, 200);
        if (!keepSnapshot || test.State == UnitTestState.Removed)
        {
            test.SourceSnapshot = null;
            test.SourceHash = null;
            test.MethodSnapshot = null;
            test.SupportFiles = null;
        }
        else if (item.SourceSnapshot is not null)
        {
            test.SourceSnapshot = item.SourceSnapshot;
            test.SourceHash = Trim(item.SourceHash, 64);
            test.MethodSnapshot = item.MethodSnapshot;
            test.SupportFiles = item.SupportFiles;
        }
    }
}

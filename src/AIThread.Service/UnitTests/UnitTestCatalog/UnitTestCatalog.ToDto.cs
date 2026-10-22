using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    // The snapshot never leaves the service layer; the DTO only says whether one is kept.
    public static UnitTestCaseDto ToDto(UnitTestCase t) => new(
        t.Id,
        t.Number,
        t.DisplayNumber,
        t.Title,
        t.Purpose,
        t.Scenario,
        t.Module,
        UnitTestText.Lines(t.Layers),
        UnitTestText.Lines(t.Keywords),
        UnitTestText.Lines(t.TargetFiles),
        t.Framework,
        t.TestProject,
        t.TestFile,
        t.ClassName,
        t.MethodName,
        t.Selector,
        UnitTestText.Lines(t.Requires),
        t.State,
        t.Origin,
        t.CreatedBy,
        t.CreatedThreadId,
        t.CreatedAt,
        t.ObsoleteReason,
        t.ObsoleteThreadId,
        t.ObsoleteBy,
        t.ObsoleteAt,
        t.QuarantineReason,
        t.LastResult,
        t.LastRunAt,
        t.LastRunBy,
        t.LastDurationMs,
        t.LastMessage,
        t.LastSkipReason,
        t.SourceSnapshot is not null);

    public static UnitTestSettingsDto ToDto(UnitTestSettings s) => new(s.Mode, s.Authoring, s.KeepSnapshots, s.TimeoutSeconds, s.UpdatedBy, s.UpdatedAt);
}
